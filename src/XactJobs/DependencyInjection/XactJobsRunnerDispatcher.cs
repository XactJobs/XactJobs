using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using XactJobs.Internal;

namespace XactJobs.DependencyInjection
{
    internal class XactJobsRunnerDispatcher<TDbContext> : BackgroundService where TDbContext: DbContext
    {
        private readonly List<Task> _runnerTasks = [];

        private readonly XactJobsOptions<TDbContext> _options;
        private readonly QuickPollChannels _quickPollChannels;
        private readonly IServiceScopeFactory _scopeFactory;

        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger<XactJobsRunnerDispatcher<TDbContext>> _logger;

        public XactJobsRunnerDispatcher(XactJobsOptions<TDbContext> options, QuickPollChannels quickPollChannels, IServiceScopeFactory scopeFactory, ILoggerFactory loggerFactory)
        {
            _options = options;
            _quickPollChannels = quickPollChannels;
            _scopeFactory = scopeFactory;
            _loggerFactory = loggerFactory;
            _logger = loggerFactory.CreateLogger<XactJobsRunnerDispatcher<TDbContext>>();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                for (var i = 0; i < _options.WorkerCount; i++)
                {
                    StartRunner(QueueNames.Default, i, _options, stoppingToken);
                }

                foreach (var (queueName, queueOptions) in _options.IsolatedQueues)
                {
                    for (var i = 0; i < queueOptions.WorkerCount; i++)
                    {
                        StartRunner(queueName, i, queueOptions, stoppingToken);
                    }
                }

                await Task.WhenAll(_runnerTasks);
            }
            catch (Exception ex)
            {
                if (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                {
                    _logger.LogError(ex, "Error executing job workers");
                }
            }

            _runnerTasks.Clear();
        }

        private void StartRunner(string queueName, int runnerIndex, XactJobsOptionsBase<TDbContext> options, CancellationToken stoppingToken)
        {
            try
            {
                if (!_quickPollChannels.Channels.TryGetValue(queueName, out var quickPollChannel))
                {
                    _logger.LogWarning("Could not find a QuickPoll channel for queue {Queue}", queueName);
                    return;
                }

                _logger.LogInformation("Starting the runner for the {Queue} queue", queueName);

                var runnerLogger = _loggerFactory.CreateLogger<XactJobRunner<TDbContext>>();

                var runner = new XactJobRunner<TDbContext>(queueName, quickPollChannel, options, _scopeFactory, runnerLogger);

                var initialDelayMs = GetDelayStepMs(options) * runnerIndex;

                _runnerTasks.Add(runner.ExecuteAsync(stoppingToken, initialDelayMs));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start the runner for the {Queue} queue", queueName ?? "default");
            }
        }

        private static int GetDelayStepMs(XactJobsOptionsBase<TDbContext> options)
        {
            return options.WorkerCount > 0 ? options.PollingIntervalInSeconds * 1000 / options.WorkerCount : 0;
        }
    }
}
