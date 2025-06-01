namespace XactJobs.TestWorker
{
    public class Worker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<Worker> _logger;

        public Worker(IServiceScopeFactory scopeFactory, ILogger<Worker> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var job = DbContextExtensions.Enqueue<TestJob>(null, x => x.RunAsync(1, "test job", Guid.NewGuid(), CancellationToken.None));

            while (!stoppingToken.IsCancellationRequested)
            {
                if (_logger.IsEnabled(LogLevel.Information))
                {
                    _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
                }

                //await _xactJobRunner.RunJobAsync(job, stoppingToken);

                await Task.Delay(1000, stoppingToken);
            }
        }
    }
}
