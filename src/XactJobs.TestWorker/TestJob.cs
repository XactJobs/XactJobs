namespace XactJobs.TestWorker
{
    public class TestJob
    {
        private readonly ILogger<TestJob> _logger;

        public TestJob(ILogger<TestJob> logger)
        {
            _logger = logger;
        }

        public Task RunAsync(int id, string name, Guid guid, CancellationToken cancellationToken)
        {
            //await _dbContext.JobDeletePeriodicAsync(name, cancellationToken)
            //    .ConfigureAwait(false);

            //throw new NotImplementedException();

            if (Random.Shared.Next(100) < 50) throw new Exception("Some transient error");

            _logger.LogInformation("Job executed: {id}, {name}, {guid}", id, name, guid);

            return Task.CompletedTask;
        }
    }

}
