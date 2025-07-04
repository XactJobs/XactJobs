﻿// This file is part of XactJobs.
//
// Portions of this file include code adapted from Hangfire, Copyright © 2013-2014 Hangfire OÜ.
// Licensed under the GNU Lesser General Public License v3.0 or later.
// See <http://www.gnu.org/licenses/>.
//
// XactJobs is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as published
// by the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// XactJobs is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU Lesser General Public License for more details.
//
// You should have received a copy of the GNU Lesser General Public License
// along with this program. If not, see <https://www.gnu.org/licenses/>.

using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace XactJobs.Internal
{
    internal static class XactJobSerializer
    {
        private static readonly ConcurrentDictionary<MethodInfo, AsyncStateMachineAttribute?> _asyncStateMachineAttributeCache = new();
        private static readonly ConcurrentDictionary<XactJobDispatchKey, MethodInfo[]> _overloadsCache = new();

        internal static XactJob FromExpression(LambdaExpression lambdaExp, DateTime? scheduleAtUtc, string? queue)
        {
            if (scheduleAtUtc.HasValue && scheduleAtUtc.Value.Kind != DateTimeKind.Utc)
            {
                throw new ArgumentException("Job scheduled time must have DateTimeKind.Utc", nameof(lambdaExp));
            }

            var callExpression = lambdaExp.Body as MethodCallExpression 
                ?? throw new ArgumentException("Expression body should be a simple method call.", nameof(lambdaExp));

            Validate(callExpression);

            var type = callExpression.Object?.Type ?? callExpression.Method.DeclaringType
                ?? throw new NotSupportedException("XactJobs does not support global methods.");

            var typeName = GetSimpleTypeName(type);

            var methodName = callExpression.Method.Name;

            var args = GetExpressionValues(callExpression.Arguments);

            var serializedArgs = JsonSerializer.Serialize(args);

            scheduleAtUtc ??= DateTime.UtcNow;

            queue ??= QueueNames.Default;

            return new XactJob(0, scheduleAtUtc.Value, typeName, methodName, serializedArgs, queue);
        }

        internal static XactJobPeriodic FromExpressionPeriodic(LambdaExpression lambdaExp, string id, string cronExp, string? queue)
        {
            var callExpression = lambdaExp.Body as MethodCallExpression 
                ?? throw new ArgumentException("Expression body should be a simple method call.", nameof(lambdaExp));

            Validate(callExpression);

            var type = callExpression.Object?.Type ?? callExpression.Method.DeclaringType
                ?? throw new NotSupportedException("XactJobs does not support global methods.");

            var typeName = GetSimpleTypeName(type);

            var methodName = callExpression.Method.Name;

            var args = GetExpressionValues(callExpression.Arguments);

            var serializedArgs = JsonSerializer.Serialize(args);

            queue ??= QueueNames.Default;

            return new XactJobPeriodic(id, DateTime.UtcNow, DateTime.UtcNow, cronExp, typeName, methodName, serializedArgs, queue, isActive: true);
        }

        internal static (Type, MethodInfo) ToMethodInfo(this XactJob job, int paramCount)
        {
            var type = Type.GetType(job.TypeName)
                ?? throw new InvalidOperationException($"Type '{job.TypeName}' could not be loaded.");

            var methods = _overloadsCache.GetOrAdd(GetMethodCacheKey(job.TypeName, job.MethodName, paramCount), _ =>
            [
                .. type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                    .Where(m => m.Name == job.MethodName && m.GetParameters().Length == paramCount)
            ]);

            var method = methods.FirstOrDefault()
                ?? throw new MissingMethodException($"Method '{job.MethodName}' with {paramCount} parameter(s) not found on type '{type.FullName}'.");

            return (type, method);
        }

        private static string GetSimpleTypeName(Type type)
        {
            if (type.IsGenericType)
            {
                var typeDefName = type.GetGenericTypeDefinition().FullName;
                var assemblyName = type.Assembly.GetName().Name;

                var genericArgs = type.GetGenericArguments()
                    .Select(GetSimpleTypeName); // recursive

                return $"{typeDefName}[{string.Join(",", genericArgs.Select(arg => $"[{arg}]"))}], {assemblyName}";
            }
            else
            {
                return string.Join(", ", type.FullName, type.Assembly.GetName().Name);
            }
        }

        private static XactJobDispatchKey GetMethodCacheKey(Type type, MethodInfo method)
        {
            return GetMethodCacheKey(GetSimpleTypeName(type), method.Name, method.GetParameters().Length);
        }

        private static XactJobDispatchKey GetMethodCacheKey(string typeName, string methodName, int paramCount)
        {
            return new XactJobDispatchKey(typeName, methodName, paramCount);
        }

        // The following method is adapted from Hangfire, Copyright © 2013-2014 Hangfire OÜ.
        // Licensed under the GNU Lesser General Public License v3.0 or later.
        // See <http://www.gnu.org/licenses/>.
        private static void Validate(MethodCallExpression callExpression)
        {
            var method = callExpression.Method;

            if (!method.IsPublic)
            {
                throw new NotSupportedException("XactJobs support only public methods.");
            }

            if (method.ContainsGenericParameters)
            {
                throw new NotSupportedException("XactJobs method can not be generic.");
            }

            if (method.ReturnType == typeof(void) &&
                _asyncStateMachineAttributeCache.GetOrAdd(method, static m => m.GetCustomAttribute<AsyncStateMachineAttribute>()) != null)
            {
                throw new NotSupportedException("XactJobs does not support async void methods. Use async Task instead.");
            }

            var parameters = method.GetParameters();

            // Check for overloads with same name and same param count
            var type = callExpression.Object?.Type ?? method.DeclaringType
                ?? throw new NotSupportedException("XactJobs does not support global methods.");

            var overloads = _overloadsCache.GetOrAdd(GetMethodCacheKey(type, method), _ =>
            [
                .. type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                    .Where(m => m.Name == method.Name && m.GetParameters().Length == parameters.Length)
            ]);

            if (overloads.Length > 1)
            {
                throw new NotSupportedException(
                    $"Type '{type.FullName}' contains multiple public methods named '{method.Name}' with {parameters.Length} parameter(s). " +
                    "This is not supported: job deserialization would be ambiguous.");
            }

            if (parameters.Length != callExpression.Arguments.Count)
            {
                throw new ArgumentException("Argument count must be equal to method parameter count.");
            }

            foreach (var parameter in parameters)
            {
                if (parameter.IsOut)
                {
                    throw new NotSupportedException("XactJobs does not support output parameters.");
                }

                if (parameter.ParameterType.IsByRef)
                {
                    throw new NotSupportedException("XactJobs does not support parameters passed by reference.");
                }

                var parameterTypeInfo = parameter.ParameterType.GetTypeInfo();
                
                if (parameterTypeInfo.IsSubclassOf(typeof(Delegate)) || parameterTypeInfo.IsSubclassOf(typeof(Expression)))
                {
                    throw new NotSupportedException("XactJobs does not support anonymous functions, delegates and lambda expressions in job method parameters.");
                }
            }
        }

        // The following method is adapted from Hangfire, Copyright © 2013-2014 Hangfire OÜ.
        // Licensed under the GNU Lesser General Public License v3.0 or later.
        // See <http://www.gnu.org/licenses/>.
        private static object?[] GetExpressionValues(IReadOnlyCollection<Expression> expressions)
        {
            var result = expressions.Count > 0 ? new object?[expressions.Count] : [];
            var index = 0;

            foreach (var expression in expressions)
            {
                result[index++] = GetExpressionValue(expression);
            }

            return result;
        }


        // The following method is adapted from Hangfire, Copyright © 2013-2014 Hangfire OÜ.
        // Licensed under the GNU Lesser General Public License v3.0 or later.
        // See <http://www.gnu.org/licenses/>.
        private static object? GetExpressionValue(Expression expression)
        {
            var value = expression is ConstantExpression constantExpression
                ? constantExpression.Value
                : Microsoft.Web.Mvc.CachedExpressionCompiler.Evaluate(expression);

            return value?.GetType() == typeof(CancellationToken) 
                ? null 
                : value;
        }
    }
}
