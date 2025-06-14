using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace XactJobs.DependencyInjection
{
    internal class XactJobsRunnerDispatcher<TDbContext> : BackgroundService where TDbContext: DbContext
    {
        private readonly List<Task> _runnerTasks = [];

        private readonly XactJobsOptions<TDbContext> _options;
        private readonly IServiceScopeFactory _scopeFactory;

        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger<XactJobsRunnerDispatcher<TDbContext>> _logger;

        public XactJobsRunnerDispatcher(XactJobsOptions<TDbContext> options, IServiceScopeFactory scopeFactory, ILoggerFactory loggerFactory)
        {
            _options = options;
            _scopeFactory = scopeFactory;
            _loggerFactory = loggerFactory;
            _logger = loggerFactory.CreateLogger<XactJobsRunnerDispatcher<TDbContext>>();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            StartRunner(null, stoppingToken);

            foreach (var queueName in _options.IsolatedQueues)
            {
                StartRunner(queueName, stoppingToken);
            }

            await Task.WhenAll(_runnerTasks);

            _runnerTasks.Clear();
        }

        private void StartRunner(string? queueName, CancellationToken stoppingToken)
        {
            try
            {
                var logger = _loggerFactory.CreateLogger<XactJobRunner<TDbContext>>();

                var runner = new XactJobRunner<TDbContext>(queueName, _options, _scopeFactory, logger);

                _runnerTasks.Add(runner.ExecuteAsync(stoppingToken));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start the runner for the {Queue} queue", queueName ?? "default");
            }
        }
    }
}
