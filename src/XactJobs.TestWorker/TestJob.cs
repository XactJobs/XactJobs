namespace XactJobs.TestWorker
{
    public class TestJob
    {
        public class TestPayload
        {
            public int PayloadId { get; set; }
            public string? PayloadData { get; set; }
        }

        private readonly ILogger<TestJob> _logger;
        private readonly QuickPoll<UserDbContext> _quickPoll;

        public TestJob(ILogger<TestJob> logger, QuickPoll<UserDbContext> quickPoll)
        {
            _logger = logger;
            _quickPoll = quickPoll;
        }

        public async Task RunTestJobAsync(int id, string name, TestPayload payload, CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(5),cancellationToken);

            //if (Random.Shared.Next(100) < 50) throw new Exception("Some transient error");

            _logger.LogInformation("Job executed: {id}, {name}", id, name);

            _quickPoll.JobEnqueue(() => RunTestJob(payload), QueueNames.Priority);
            
            await _quickPoll.SaveChangesAndNotifyAsync(cancellationToken);

            //return Task.CompletedTask;
        }

        public void RunTestJob(TestPayload payload)
        {
            _logger.LogInformation("Job executed: {payloadId}, {payloadData}", payload.PayloadId, payload.PayloadData);
        }
    }

}
