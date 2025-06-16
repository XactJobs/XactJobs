using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace XactJobs.DependencyInjection
{
    internal class XactJobsCronScheduler<TDbContext> : BackgroundService where TDbContext : DbContext
    {
        private readonly XactJobsOptions<TDbContext> _options;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<XactJobsCronScheduler<TDbContext>> _logger;

        public XactJobsCronScheduler(XactJobsOptions<TDbContext> options, IServiceScopeFactory scopeFactory, ILogger<XactJobsCronScheduler<TDbContext>> logger)
        {
            _options = options;
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            const int MaxRetries = 10;

            for (var i = 0; i < MaxRetries; i++)
            {
                try
                {
                    if (i > 0)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken)
                            .ConfigureAwait(false);
                    }

                    await EnsurePeriodicJobs(stoppingToken)
                        .ConfigureAwait(false);

                    break;
                }
                catch (Exception ex)
                {
                    if (ex is OperationCanceledException && stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }

                    if (i == MaxRetries - 1)
                    {
                        _logger.LogError(ex, "Failed to create periodic jobs");
                    }
                }
            }
            
        }

        private async Task EnsurePeriodicJobs(CancellationToken stoppingToken)
        {
            using var scope = _scopeFactory.CreateScope();

            var db = scope.ServiceProvider.GetRequiredService<TDbContext>();

            var periodicJobs = await db.Set<XactJobPeriodic>()
                .ToListAsync(stoppingToken)
                .ConfigureAwait(false);

            using var tx = db.Database.BeginTransaction();
            try
            {
                await EnsurePeriodicJobsForQueue(db, null, _options, periodicJobs, stoppingToken)
                    .ConfigureAwait(false);

                foreach (var (queueName, queueOptions) in _options.IsolatedQueues)
                {
                    await EnsurePeriodicJobsForQueue(db, queueName, queueOptions, periodicJobs, stoppingToken)
                        .ConfigureAwait(false);
                }

                await tx.CommitAsync(stoppingToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                await tx.RollbackAsync(stoppingToken)
                    .ConfigureAwait(false);

                throw;
            }
        }

        private static async Task EnsurePeriodicJobsForQueue(TDbContext db,
                                                             string? queueName,
                                                             XactJobsOptionsBase<TDbContext> options,
                                                             List<XactJobPeriodic> periodicJobs,
                                                             CancellationToken stoppingToken)
        {
            foreach (var (name, (lambdaExp, cronExp, isActive)) in options.PeriodicJobs)
            {
                var periodicJob = periodicJobs.FirstOrDefault(j => j.Name == name);

                if (periodicJob == null)
                {
                    periodicJob = db.AddJobPeriodic(lambdaExp, name, cronExp, null);
                }
                else
                {
                    var templateJob = XactJobSerializer.FromExpressionPeriodic(lambdaExp, Guid.Empty, name, cronExp, queueName);

                    if (!periodicJob.IsCompatibleWith(templateJob))
                    {
                        // delete existing queued jobs for this periodic definition
                        await db.Set<XactJob>()
                            .Where(x => x.PeriodicJobId == periodicJob.Id)
                            .ExecuteDeleteAsync(stoppingToken)
                            .ConfigureAwait(false);

                        // update the definition
                        periodicJob.UpdateDefinition(templateJob);

                        // schedule the next run
                        db.ScheduleNextRun(periodicJob);
                    }
                }

                periodicJob.Activate(isActive);
            }

            await db.SaveChangesAsync(stoppingToken)
                .ConfigureAwait(false);
        }
    }
}
