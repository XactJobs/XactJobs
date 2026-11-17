// Copyright (c) 2025 XactJobs
// Licensed under the MIT License. See LICENSE in the repository root.

using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace XactJobs.Internal
{
    internal class XactJobCompiler
    {
        private static readonly ConcurrentDictionary<XactJobDispatchKey, (Func<IServiceProvider, object?[], Task?>, ParameterInfo[])> _compiledJobs = new();

        /// <summary>Per job method, its <see cref="XactRetryAttribute"/>'s strategy, or null without one.</summary>
        private static readonly ConcurrentDictionary<XactJobDispatchKey, IRetryStrategy?> _retryStrategies = new();

        /// <summary>
        /// The strategy of the job method's <see cref="XactRetryAttribute"/>, or null without one. Only when an attempt fails. Throws if
        /// the method can't be found (not cached, as for running it). An invalid attribute (changed since the job was enqueued, which
        /// refuses an invalid one) is cached as none, and throws <see cref="ArgumentOutOfRangeException"/> only the first time, for the
        /// log.
        /// </summary>
        public static IRetryStrategy? FindRetryStrategy(XactJob job)
        {
            var argCount = CountArguments(job.MethodArgs);

            var key = new XactJobDispatchKey(job.TypeName, job.MethodName, argCount);

            if (_retryStrategies.TryGetValue(key, out var strategy))
            {
                return strategy;
            }

            var (_, method) = job.ToMethodInfo(argCount);

            try
            {
                strategy = method.GetCustomAttribute<XactRetryAttribute>(inherit: true)?.ToRetryStrategy();
            }
            catch (ArgumentOutOfRangeException)
            {
                if (_retryStrategies.TryAdd(key, null)) throw;

                return null;
            }

            return _retryStrategies.GetOrAdd(key, strategy);
        }

        /// <summary>The number of elements of the arguments' JSON array, without parsing them into a document.</summary>
        private static int CountArguments(string methodArgs)
        {
            var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(methodArgs));

            reader.Read();

            var count = 0;

            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                count++;

                // an object or an array argument: to its end
                reader.Skip();
            }

            return count;
        }

        public static async Task CompileAndRunJobAsync(IServiceScope scope, XactJob job, XactJobContext context, CancellationToken stoppingToken)
        {
            var (compiledFunc, args) = PrepareCall(job, context, stoppingToken);

            var resultTask = compiledFunc(scope.ServiceProvider, args);

            if (resultTask != null)
            {
                await resultTask.ConfigureAwait(false);
            }
        }

        /// <summary>
        /// The job's compiled method, and its arguments. Before the job runs, so that the parsed JSON's buffers go back to the pool
        /// right away, not when the job finishes (the arguments are deserialized into objects of their own).
        /// </summary>
        private static (Func<IServiceProvider, object?[], Task?>, object?[]) PrepareCall(XactJob job, XactJobContext context, CancellationToken stoppingToken)
        {
            using var jsonArgs = JsonDocument.Parse(job.MethodArgs);

            var args = new object?[jsonArgs.RootElement.GetArrayLength()];

            var key = new XactJobDispatchKey(job.TypeName, job.MethodName, args.Length);

            // static, with the job as the factory's argument: a lambda capturing it would be allocated also when the job is cached
            var (compiledFunc, parameters) = _compiledJobs.GetOrAdd(key, static (key, job) =>
            {
                var (type, method) = job.ToMethodInfo(key.ArgCount);
                return (BuildJobDelegate(type, method), method.GetParameters());
            }, job);

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

            return (compiledFunc, args);
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
