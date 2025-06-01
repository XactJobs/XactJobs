namespace XactJobs.TestWorker
{
    public class TestJob
    {
        private readonly ILogger<TestJob> _logger;

        public TestJob(ILogger<TestJob> logger)
        {
            _logger = logger;
        }

        public void Run(int id, string name, Guid guid)
        {
            _logger.LogInformation("Job executed: {id}, {name}, {guid}", id, name, guid);
        }

        public async Task RunAsync(int id, string name, Guid guid, CancellationToken cancellationToken)
        {
            await Task.Delay(5000, cancellationToken);
            _logger.LogInformation("Job executed: {id}, {name}, {guid}", id, name, guid);
        }

        public static async Task RunStaticAsync(int id, string name, Guid guid)
        {
            await Task.Delay(5000);
            Console.WriteLine($"Job executed: {id}, {name}, {guid}");
        }

    }

}
