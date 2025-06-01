using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;

namespace XactJobs
{
    public sealed class XactJobRunner<TDbContext> where TDbContext: DbContext
    {
        private static readonly ConcurrentDictionary<XactJobDispatchKey, Func<IServiceProvider, object?[], CancellationToken, Task?>> _compiledJobs = new();

        private readonly XactJobsOptions _options;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger _logger;

        public XactJobRunner(XactJobsOptions options, IServiceScopeFactory scopeFactory, ILogger logger)
        {
            _options = options;
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var sql = _options.Dialect.GetFetchJobsSql(_options.BatchSize);

            var parallelOptions = new ParallelOptions
            {
                CancellationToken = stoppingToken,
                MaxDegreeOfParallelism = _options.MaxDegreeOfParallelism
            };

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunJobsAsync(sql, parallelOptions, stoppingToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Running Jobs failed. Retrying in 10 seconds");

                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                }
            }
        }

        private async Task RunJobsAsync(string sql, ParallelOptions parallelOptions, CancellationToken stoppingToken)
        {
            using var scope = _scopeFactory.CreateScope();

            var dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();

            using var xact = await dbContext.Database.BeginTransactionAsync(stoppingToken)
                .ConfigureAwait(false);

            var jobs = await dbContext.Set<XactJob>().FromSqlRaw(sql)
                .ToListAsync(stoppingToken)
                .ConfigureAwait(false);

            await Parallel.ForEachAsync(jobs, parallelOptions, async (job, stoppingToken) =>
            {
                try
                {
                    await RunJobAsync(job, stoppingToken)
                        .ConfigureAwait(false);

                    job.MarkCompleted();

                    if (_logger.IsEnabled(LogLevel.Debug))
                    {
                        _logger.LogDebug("Job completed: {TypeName}.{MethodName} ({Id})", job.TypeName, job.MethodName, job.Id);
                    }
                }
                catch (Exception ex)
                {
                    job.MarkFailed(ex);
                    _logger.LogError(ex, "Job failed: {TypeName}.{MethodName} ({Id})", job.TypeName, job.MethodName, job.Id);
                }
            })
                .ConfigureAwait(false);

            await dbContext.SaveChangesAsync(stoppingToken)
                .ConfigureAwait(false);

            await xact.CommitAsync(stoppingToken)
                .ConfigureAwait(false);
        }

        public async Task RunJobAsync(XactJob job, CancellationToken stoppingToken)
        {
            var key = new XactJobDispatchKey(job.TypeName, job.MethodName, job.MethodArgs.Length);

            var compiled = _compiledJobs.GetOrAdd(key, k =>
            {
                var (type, method) = job.ToMethodInfo();
                return BuildJobDelegate(type, method);
            });

            using var scope = _scopeFactory.CreateScope();

            var args = JsonSerializer.Deserialize<object?[]>(job.MethodArgs) ?? [];

            var resultTask = compiled(scope.ServiceProvider, args, stoppingToken);

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
