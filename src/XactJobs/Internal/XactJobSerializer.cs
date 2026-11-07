// This file is part of XactJobs.
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
        private static readonly ConcurrentDictionary<MethodInfo, string> _methodSignatureCache = new();

        internal static XactJob FromExpression(LambdaExpression lambdaExp,
                                               DateTime? scheduleAtUtc,
                                               string? queue,
                                               Guid? batchId = null,
                                               Guid? awaitsBatchId = null,
                                               XactJobContinuation? continuationCondition = null)
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

            var methodName = GetMethodSignature(callExpression.Method);

            var args = GetExpressionValues(callExpression.Arguments);

            var serializedArgs = JsonSerializer.Serialize(args);

            scheduleAtUtc ??= DateTime.UtcNow;

            queue ??= QueueNames.Default;

            return new XactJob(0, scheduleAtUtc.Value, typeName, methodName, serializedArgs, queue)
            {
                BatchId = batchId,
                AwaitsBatchId = awaitsBatchId,
                ContinuationCondition = continuationCondition
            };
        }

        internal static XactJobPeriodic FromExpressionPeriodic(LambdaExpression lambdaExp, string id, string cronExp, string? queue)
        {
            var callExpression = lambdaExp.Body as MethodCallExpression 
                ?? throw new ArgumentException("Expression body should be a simple method call.", nameof(lambdaExp));

            Validate(callExpression);

            var type = callExpression.Object?.Type ?? callExpression.Method.DeclaringType
                ?? throw new NotSupportedException("XactJobs does not support global methods.");

            var typeName = GetSimpleTypeName(type);

            var methodName = GetMethodSignature(callExpression.Method);

            var args = GetExpressionValues(callExpression.Arguments);

            var serializedArgs = JsonSerializer.Serialize(args);

            queue ??= QueueNames.Default;

            return new XactJobPeriodic(id, DateTime.UtcNow, DateTime.UtcNow, cronExp, typeName, methodName, serializedArgs, queue, isActive: true, version: XactJobPeriodic.NewVersion());
        }

        internal static (Type, MethodInfo) ToMethodInfo(this XactJob job, int paramCount)
        {
            var type = Type.GetType(job.TypeName)
                ?? throw new InvalidOperationException($"Type '{job.TypeName}' could not be loaded.");

            // new jobs store the method's signature, e.g. "Send(System.String, System.Int32)",
            // and jobs from before 0.1.7 only its name
            var signatureStart = job.MethodName.IndexOf('(');
            var methodName = signatureStart < 0 ? job.MethodName : job.MethodName[..signatureStart];

            var candidates = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .Where(m => m.Name == methodName && m.GetParameters().Length == paramCount)
                .ToArray();

            if (candidates.Length == 0)
            {
                throw new MissingMethodException($"Method '{methodName}' with {paramCount} parameter(s) not found on type '{type.FullName}'.");
            }

            if (signatureStart >= 0)
            {
                var matches = candidates.Where(m => GetMethodSignature(m) == job.MethodName).ToArray();

                if (matches.Length > 0)
                {
                    return (type, MostDerived(matches));
                }
            }

            // no signature, or the parameter types changed (e.g. a type was renamed): the method
            // is still found by its name and parameter count, if it has no overloads
            if (candidates.Select(GetMethodSignature).Distinct().Count() == 1)
            {
                return (type, MostDerived(candidates));
            }

            throw new AmbiguousMatchException(
                $"Type '{type.FullName}' has several public methods named '{methodName}' with {paramCount} parameter(s), " +
                $"and none of them matches the job's method '{job.MethodName}'.");
        }

        /// <summary>
        /// A method hidden with <c>new</c> is returned along with the method that hides it: this picks the one that hides it.
        /// </summary>
        private static MethodInfo MostDerived(MethodInfo[] methods)
        {
            return methods.Length == 1
                ? methods[0]
                : methods.OrderByDescending(m => InheritanceDepth(m.DeclaringType)).First();
        }

        private static int InheritanceDepth(Type? type)
        {
            var depth = 0;

            for (; type != null; type = type.BaseType)
            {
                depth++;
            }

            return depth;
        }

        /// <summary>
        /// The method's name and parameter types, e.g. "Send(System.String, System.Int32)", which tells its overloads apart.
        /// The types have no assembly names, so that pending jobs still run after a type moves to another assembly.
        /// </summary>
        internal static string GetMethodSignature(MethodInfo method)
        {
            return _methodSignatureCache.GetOrAdd(method, FormatMethodSignature);
        }

        private static string FormatMethodSignature(MethodInfo method)
        {
            var parameterTypes = method.GetParameters().Select(p => GetSignatureTypeName(p.ParameterType));

            return $"{method.Name}({string.Join(", ", parameterTypes)})";
        }

        private static string GetSignatureTypeName(Type type)
        {
            if (type.IsArray)
            {
                return $"{GetSignatureTypeName(type.GetElementType()!)}[{new string(',', type.GetArrayRank() - 1)}]";
            }

            if (type.IsGenericType)
            {
                var genericArgs = type.GetGenericArguments().Select(GetSignatureTypeName); // recursive

                return $"{type.GetGenericTypeDefinition().FullName}[{string.Join(", ", genericArgs)}]";
            }

            return type.FullName ?? type.Name;
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

            // the worker passes its own (see XactJobCompiler)
            return value is CancellationToken or XactJobContext
                ? null
                : value;
        }
    }
}
