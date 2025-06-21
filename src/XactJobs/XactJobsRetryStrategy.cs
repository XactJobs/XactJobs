namespace XactJobs
{
    public interface IXactJobsRetryStrategy
    {
        DateTime? GetRetryTimeUtc(XactJob job, int newErrorCount);
    }

    public class XactJobsDefaultRetryStrategy : IXactJobsRetryStrategy
    {
        public int MaxAttempts { get; }
        public IReadOnlyList<int> RetrySeconds { get; }

        public XactJobsDefaultRetryStrategy(int maxAttempts = 10, int[]? retrySeconds = null)
        {
            MaxAttempts = maxAttempts;
            RetrySeconds = retrySeconds ?? [2, 2, 5, 10, 30, 60, 5 * 60, 15 * 60, 30 * 60, 60 * 60]; 
        }

        public DateTime? GetRetryTimeUtc(XactJob job, int newErrorCount)
        {
            if (newErrorCount >= MaxAttempts)
            {
                return null;
            }

            var seconds = newErrorCount <= RetrySeconds.Count
                ? RetrySeconds[newErrorCount - 1]
                : RetrySeconds[^1];

            return DateTime.UtcNow.AddSeconds(seconds);
        }
    }
}
