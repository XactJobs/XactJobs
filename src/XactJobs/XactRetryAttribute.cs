// Copyright (c) 2025 XactJobs
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Reflection;

namespace XactJobs
{
    /// <summary>
    /// The retries of a job method, in place of its queue's retry strategy: up to <see cref="MaxRetries"/> retries, waiting
    /// <see cref="RetrySeconds"/> before each one, as <see cref="DefaultRetryStrategy"/> does.
    /// <para>
    /// <c>[XactRetry(0)]</c> runs the job once, without retries. <c>[XactRetry(3, [10, 60, 300])]</c> retries it up to 3 times: 10 seconds,
    /// a minute, then 5 minutes after each failed attempt. A job that throws <see cref="XactJobCancelException"/> isn't retried.
    /// </para>
    /// <para>
    /// It's read when an attempt fails, so a change of it applies to the jobs already enqueued too. An invalid one (negative retries or
    /// waits) is refused when the job is enqueued.
    /// </para>
    /// </summary>
    /// <param name="maxRetries">The retries after the first attempt: 0 is none.</param>
    /// <param name="retrySeconds">The wait before each retry, in seconds, the last one for the retries beyond the list. None:
    /// <see cref="DefaultRetryStrategy.DefaultRetrySeconds"/>.</param>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public sealed class XactRetryAttribute(int maxRetries, int[]? retrySeconds = null) : Attribute
    {
        public int MaxRetries { get; } = maxRetries;

        public IReadOnlyList<int> RetrySeconds { get; } = retrySeconds ?? [];

        /// <summary>
        /// The retries of a job method: its attribute's strategy, or why the attribute is invalid. Null without one. Read from the method
        /// the runner calls (an override inherits it), by the runner and by the checks at enqueue and at start.
        /// </summary>
        internal static XactJobRetry? ReadFrom(MethodInfo method)
        {
            var attribute = method.GetCustomAttribute<XactRetryAttribute>(inherit: true);

            if (attribute == null) return null;

            return DefaultRetryStrategy.Validate(attribute.MaxRetries, attribute.RetrySeconds) is { } error
                ? new XactJobRetry(null, error)
                : new XactJobRetry(new DefaultRetryStrategy(attribute.MaxRetries, [.. attribute.RetrySeconds]), null);
        }
    }

    /// <summary>A job method's <see cref="XactRetryAttribute"/>: its strategy, or why it's invalid (then the queue's strategy is used).</summary>
    internal sealed class XactJobRetry(IRetryStrategy? strategy, string? invalidReason)
    {
        private int _isInvalidReasonLogged;

        public IRetryStrategy? Strategy { get; } = strategy;

        public string? InvalidReason { get; } = invalidReason;

        /// <summary>True the first time only: an invalid attribute is logged once per method.</summary>
        public bool TryMarkInvalidReasonLogged() => Interlocked.Exchange(ref _isInvalidReasonLogged, 1) == 0;
    }
}
