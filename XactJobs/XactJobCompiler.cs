using Microsoft.Extensions.DependencyInjection;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;

namespace XactJobs
{
    internal class XactJobCompiler
    {
        // not using concurrent dictionary because GetOrAdd suffers from the "stampede" problem
        private static readonly Dictionary<XactJobDispatchKey, (Func<IServiceProvider, object?[], CancellationToken, Task?>, ParameterInfo[])> _compiledJobs = new();
        
        public static async Task CompileAndRunJobAsync(IServiceScope scope, XactJob job, CancellationToken stoppingToken)
        {
            var jsonArgs = JsonDocument.Parse(job.MethodArgs);

            var args = new object?[jsonArgs.RootElement.GetArrayLength()];

            var key = new XactJobDispatchKey(job.TypeName, job.MethodName, args.Length);

            Func<IServiceProvider, object?[], CancellationToken, Task?> compiledFunc;

            ParameterInfo[] parameters;

            lock (_compiledJobs)
            {
                if (!_compiledJobs.TryGetValue(key, out var compiledJob))
                {
                    var (type, method) = job.ToMethodInfo(args.Length);

                    compiledJob = (BuildJobDelegate(type, method), method.GetParameters());

                    _compiledJobs.Add(key, compiledJob);   
                }

                compiledFunc = compiledJob.Item1;
                parameters = compiledJob.Item2;
            }

            // set the args
            for (var i = 0; i < args.Length; i++)
            {
                args[i] = JsonSerializer.Deserialize(jsonArgs.RootElement[i], parameters[i].ParameterType);
            }

            var resultTask = compiledFunc(scope.ServiceProvider, args, stoppingToken);

            if (resultTask != null)
            {
                await resultTask.ConfigureAwait(false);
            }
        }

        public static Func<IServiceProvider, object?[], CancellationToken, Task?> BuildJobDelegate(Type type, MethodInfo method)
        {
            var spParam = Expression.Parameter(typeof(IServiceProvider), "serviceProvider");
            var argsParam = Expression.Parameter(typeof(object[]), "args");
            var ctParam = Expression.Parameter(typeof(CancellationToken), "stoppingToken");

            var parameters = method.GetParameters();
            var callArgs = new Expression[parameters.Length];

            for (int i = 0; i < parameters.Length; i++)
            {
                var argAccess = Expression.ArrayIndex(argsParam, Expression.Constant(i));
                var paramType = parameters[i].ParameterType;

                if (paramType == typeof(CancellationToken))
                {
                    callArgs[i] = ctParam;
                }
                else
                {
                    callArgs[i] = Expression.Convert(argAccess, paramType);
                }
            }

            Expression? instanceExpr = method.IsStatic
                ? null
                : Expression.Convert(
                    Expression.Call(typeof(ServiceProviderServiceExtensions), nameof(ServiceProviderServiceExtensions.GetRequiredService), [type], spParam),
                    type
                );

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

            var lambda = Expression.Lambda<Func<IServiceProvider, object?[], CancellationToken, Task?>>(
                bodyExpr, spParam, argsParam, ctParam);

            return lambda.Compile();
        }
    }
}
