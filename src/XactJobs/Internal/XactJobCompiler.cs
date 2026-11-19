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
        /// <summary>Per job method: its delegate, the method and its parameters, and its <see cref="XactRetryAttribute"/> (null without one).</summary>
        private static readonly ConcurrentDictionary<(string TypeName, string MethodName), (Func<IServiceProvider, object?[], Task?>, MethodInfo, ParameterInfo[], XactJobRetry?)> _compiledJobs = new();

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

            var args = new object?[jsonArgs.RootElement.GetArrayLength()];

            // static, with the job as the factory's argument: a lambda capturing it would be allocated also when the job is cached.
            // The method name has the parameter types, so it tells overloads apart. Not found: XactJobNotFoundException, not cached
            var (compiledFunc, method, parameters, retry) = _compiledJobs.GetOrAdd((job.TypeName, job.MethodName), static (_, job) =>
            {
                var (type, method) = job.ToMethodInfo();
                return (BuildJobDelegate(type, method), method, method.GetParameters(), XactRetryAttribute.ReadFrom(method));
            }, job);

            // before the arguments, which can fail too: a failed attempt is retried as its method says
            context.Retry = retry;

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
