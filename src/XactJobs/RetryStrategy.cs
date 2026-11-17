// Copyright (c) 2025 XactJobs
// Licensed under the MIT License. See LICENSE in the repository root.

namespace XactJobs
{
    /// <summary>
    /// When a failed job runs again. Set per queue with <c>WithRetryStrategy</c>; a job method's <see cref="XactRetryAttribute"/> replaces
    /// it for that method. A job that throws <see cref="XactJobCancelException"/> is never retried, and the strategy isn't asked.
    /// </summary>
    public interface IRetryStrategy
    {
        /// <summary>
        /// The time of the next attempt in UTC, or null for no more attempts: the job is then recorded as cancelled.
        /// </summary>
        /// <param name="job">The job that failed. For the first attempt of a continuation, its <c>ScheduledAt</c> is the reserved time
        /// 9999-12-31: base the retry time on the current time.</param>
        /// <param name="newErrorCount">The failed attempts so far, this one included: 1 after the first.</param>
        /// <param name="exception">What the attempt threw, e.g. to retry a timeout sooner than a rate limit.</param>
        DateTime? GetRetryTimeUtc(XactJob job, int newErrorCount, Exception exception);
    }

    /// <summary>
    /// Up to <see cref="MaxRetries"/> retries, the first one <see cref="RetrySeconds"/>[0] seconds after the failed attempt, the second one
    /// [1] seconds after the next, and so on, with the last wait for the retries beyond the list.
    /// </summary>
    public class DefaultRetryStrategy : IRetryStrategy
    {
        /// <summary>The waits by default, in seconds, one per default retry: 2 at first, then longer, up to an hour.</summary>
        public static IReadOnlyList<int> DefaultRetrySeconds { get; } = [2, 2, 5, 10, 30, 60, 5 * 60, 15 * 60, 30 * 60, 60 * 60];

        public int MaxRetries { get; }
        public IReadOnlyList<int> RetrySeconds { get; }

        /// <param name="maxRetries">The retries after the first attempt: 0 is none.</param>
        /// <param name="retrySeconds">The wait before each retry, in seconds. Null or empty: <see cref="DefaultRetrySeconds"/>.</param>
        public DefaultRetryStrategy(int maxRetries = 10, int[]? retrySeconds = null)
        {
            if (Validate(maxRetries, retrySeconds) is { } error)
            {
                throw new ArgumentOutOfRangeException(maxRetries < 0 ? nameof(maxRetries) : nameof(retrySeconds), error);
            }

            MaxRetries = maxRetries;
            RetrySeconds = retrySeconds is { Length: > 0 } ? [.. retrySeconds] : DefaultRetrySeconds;
        }

        /// <summary>Why the values are invalid, or null: negative retries or waits.</summary>
        internal static string? Validate(int maxRetries, IReadOnlyList<int>? retrySeconds)
        {
            if (maxRetries < 0) return $"the retries can't be negative ({maxRetries})";

            if (retrySeconds != null && retrySeconds.Any(s => s < 0)) return "a wait can't be negative";

            return null;
        }

        public DateTime? GetRetryTimeUtc(XactJob job, int newErrorCount, Exception exception)
        {
            // newErrorCount is also the number of the retry it would be
            if (newErrorCount > MaxRetries)
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
