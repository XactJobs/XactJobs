using XactJobs.TestModel;

namespace XactJobs.TestWorker
{
    public class TestJob
    {
        private readonly UserDbContext _dbContext;
        private readonly ILogger<TestJob> _logger;

        public TestJob(UserDbContext dbContext, ILogger<TestJob> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }

        public Task RunAsync(int id, string name, Guid guid, CancellationToken cancellationToken)
        {
            //await _dbContext.JobDeletePeriodicAsync(name, cancellationToken)
            //    .ConfigureAwait(false);

            //throw new NotImplementedException();

            _logger.LogInformation("Job executed: {id}, {name}, {guid}", id, name, guid);

            return Task.CompletedTask;
        }
    }

}
