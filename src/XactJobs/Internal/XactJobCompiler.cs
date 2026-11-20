// Copyright (c) 2025 XactJobs
// Licensed under the MIT License. See LICENSE in the repository root.

using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;

namespace XactJobs.Internal
{
    /// <summary>A job's method with its arguments, ready to run.</summary>
    internal readonly struct XactJobCall(Func<IServiceProvider, object?[], Task?> compiledFunc, MethodInfo method, object?[] arguments)
    {
        public MethodInfo Method { get; } = method;

        public object?[] Arguments { get; } = arguments;

        /// <summary>Runs the job method, with its instance from the services if it isn't static.</summary>
        public Task InvokeAsync(IServiceProvider services) => compiledFunc(services, Arguments) ?? Task.CompletedTask;
    }

    internal class XactJobCompiler
    {
        /// <summary>
        /// Per stored job method: its delegate, the method and its parameters, its <see cref="XactRetryAttribute"/> (null without one),
        /// and the values of the optional parameters the method has after the stored ones (empty if it has the stored signature).
        /// </summary>
        private static readonly ConcurrentDictionary<(string TypeName, string MethodName), (Func<IServiceProvider, object?[], Task?>, MethodInfo, ParameterInfo[], XactJobRetry?, object?[])> _compiledJobs = new();

        public static Task CompileAndRunJobAsync(IServiceScope scope, XactJob job, XactJobContext context, CancellationToken stoppingToken)
        {
            return PrepareCall(job, context, stoppingToken).InvokeAsync(scope.ServiceProvider);
        }

        /// <summary>
        /// The job's compiled method, and its arguments. Before the job runs, so that the parsed JSON's buffers go back to the pool
        /// right away, not when the job finishes (the arguments are deserialized into objects of their own).
        /// </summary>
        public static XactJobCall PrepareCall(XactJob job, XactJobContext context, CancellationToken stoppingToken)
        {
            using var jsonArgs = JsonDocument.Parse(job.MethodArgs);

            // static, with the job as the factory's argument: a lambda capturing it would be allocated also when the job is cached.
            // The method name has the parameter types, so it tells overloads apart. Not found: XactJobNotFoundException, not cached
            // The stored arguments are one per parameter of the stored signature, the same for each job of the key
            var (compiledFunc, method, parameters, retry, defaults) = _compiledJobs.GetOrAdd((job.TypeName, job.MethodName), static (_, state) =>
            {
                var (type, method) = state.job.ToMethodInfo();
                var parameters = method.GetParameters();

                return (BuildJobDelegate(type, method), method, parameters, XactRetryAttribute.ReadFrom(method), DefaultValues(parameters, state.stored));
            }, (job, stored: jsonArgs.RootElement.GetArrayLength()));

            // before the arguments, which can fail too: a failed attempt is retried as its method says
            context.Retry = retry;

            var stored = parameters.Length - defaults.Length;
            var args = new object?[parameters.Length];

            // set the args
            for (var i = 0; i < args.Length; i++)
            {
                if (parameters[i].ParameterType == typeof(CancellationToken))
                {
                    args[i] = stoppingToken;
                }
                else if (parameters[i].ParameterType == typeof(XactJobContext))
                {
                    args[i] = context;
                }
                else if (i >= stored)
                {
                    // an optional parameter added since the job was stored
                    args[i] = defaults[i - stored];
                }
                else if (jsonArgs.RootElement[i].ValueKind == JsonValueKind.Null)
                {
                    args[i] = null;
                }
                else
                {
                    args[i] = jsonArgs.RootElement[i].Deserialize(parameters[i].ParameterType);
                }
            }

            return new XactJobCall(compiledFunc, method, args);
        }

        /// <summary>The values of the parameters after the first <paramref name="stored"/> ones, all optional (see FindMethod).</summary>
        private static object?[] DefaultValues(ParameterInfo[] parameters, int stored)
        {
            if (stored >= parameters.Length) return [];

            var values = new object?[parameters.Length - stored];

            for (var i = stored; i < parameters.Length; i++)
            {
                var parameter = parameters[i];
                var value = parameter.HasDefaultValue ? parameter.DefaultValue : null;

                // "= default" of a struct (a CancellationToken, a DateTime), or [Optional] without a value: the type's default
                if (value == null && parameter.ParameterType.IsValueType && Nullable.GetUnderlyingType(parameter.ParameterType) == null)
                {
                    value = Activator.CreateInstance(parameter.ParameterType);
                }

                values[i - stored] = value;
            }

            return values;
        }

        public static Func<IServiceProvider, object?[], Task?> BuildJobDelegate(Type type, MethodInfo method)
        {
            var spParam = Expression.Parameter(typeof(IServiceProvider), "serviceProvider");
            var argsParam = Expression.Parameter(typeof(object[]), "args");

            var parameters = method.GetParameters();
            var callArgs = new Expression[parameters.Length];

            for (int i = 0; i < parameters.Length; i++)
            {
                var argAccess = Expression.ArrayIndex(argsParam, Expression.Constant(i));
                var paramType = parameters[i].ParameterType;

                callArgs[i] = Expression.Convert(argAccess, paramType);
            }

            Expression? instanceExpr = null;

            if (!method.IsStatic)
            {
                // Find first constructor
                var ctor = type.GetConstructors().FirstOrDefault() 
                    ?? throw new InvalidOperationException($"Type {type.FullName} does not have a public constructor.");

                var ctorParams = ctor.GetParameters();

                var ctorArgs = ctorParams
                    .Select(p =>
                        (Expression)Expression.Call(
                            typeof(ServiceProviderServiceExtensions),
                            nameof(ServiceProviderServiceExtensions.GetRequiredService),
                            [p.ParameterType],
                            spParam))
                    .ToArray();

                instanceExpr = Expression.New(ctor, ctorArgs);
            }

            var callExpr = Expression.Call(instanceExpr, method, callArgs);

            Expression bodyExpr;

            if (typeof(Task).IsAssignableFrom(method.ReturnType))
            {
                bodyExpr = Expression.Convert(callExpr, typeof(Task));
            }
            else
            {
                var completedTaskProp = typeof(Task).GetProperty(nameof(Task.CompletedTask))!;
                var completedTaskExpr = Expression.Property(null, completedTaskProp);

                // callExpr returns void, so use a Block to sequence call + completedTask
                bodyExpr = Expression.Block(callExpr, completedTaskExpr);
            }

            var lambda = Expression.Lambda<Func<IServiceProvider, object?[], Task?>>(
                bodyExpr, spParam, argsParam);

            return lambda.Compile();
        }
    }
}
