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
            using var scope = _scopeFactory.CreateScope();

            var db = scope.ServiceProvider.GetRequiredService<TDbContext>();

            foreach (var (id, (lambdaExp, cronExp)) in _options.PeriodicJobs)
            {
                var existing = await db.Set<XactJobPeriodic>().FindAsync([id], stoppingToken)
                    .ConfigureAwait(false);

                if (existing == null)
                {
                    db.AddJobPeriodic(lambdaExp, id, cronExp);
                }
            }

            await db.SaveChangesAsync(stoppingToken)
                .ConfigureAwait(false);
        }
    }
}
