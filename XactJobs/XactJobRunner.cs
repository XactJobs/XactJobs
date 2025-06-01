using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace XactJobs
{
    public sealed class XactJobRunner
    {
        private static readonly ConcurrentDictionary<XactJobDispatchKey, Func<IServiceProvider, object?[], CancellationToken, Task?>> _compiledJobs = new();

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger _logger;

        public XactJobRunner(IServiceScopeFactory scopeFactory, ILogger logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public async Task RunJobAsync(XactJob job, CancellationToken stoppingToken)
        {
            var key = new XactJobDispatchKey(job.TypeName, job.MethodName, job.Args.Length);

            var compiled = _compiledJobs.GetOrAdd(key, k =>
            {
                var (type, method) = job.ToMethodInfo();
                return BuildJobDelegate(type, method);
            });

            using var scope = _scopeFactory.CreateScope();

            var resultTask = compiled(scope.ServiceProvider, job.Args, stoppingToken);

            if (resultTask != null)
            {
                await resultTask.ConfigureAwait(false);
            }
        }

        private static Func<IServiceProvider, object?[], CancellationToken, Task?> BuildJobDelegate(Type type, MethodInfo method)
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
