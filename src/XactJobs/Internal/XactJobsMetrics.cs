// Copyright (c) 2025 XactJobs
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace XactJobs.Internal
{
    /// <summary>
    /// The workers' metrics, on the meter "XactJobs" (System.Diagnostics.Metrics): an app collects them with OpenTelemetry
    /// (<c>AddMeter("XactJobs")</c>), dotnet-counters or a MeterListener. Nothing is recorded while nothing listens.
    /// </summary>
    internal sealed class XactJobsMetrics
    {
        public const string MeterName = "XactJobs";

        // outcomes of an attempt
        public const string Completed = "completed";
        public const string Skipped = "skipped";
        public const string Retried = "retried";
        public const string Failed = "failed";
        public const string Interrupted = "interrupted";

        private const string QueueTag = "xactjobs.queue";
        private const string OutcomeTag = "xactjobs.outcome";
        private const string JobTypeTag = "xactjobs.job.type";
        private const string JobMethodTag = "xactjobs.job.method";

        private static readonly Meter Meter = new(MeterName, typeof(XactJobsMetrics).Assembly.GetName().Version?.ToString(3));

        private static readonly Counter<long> Attempts = Meter.CreateCounter<long>("xactjobs.job.attempts", "{attempt}",
            "Attempts of jobs that ended, by outcome: completed, skipped (an inactive periodic job, or a continuation whose condition " +
            "isn't met), retried (failed, and scheduled again), failed (out of retries, or cancelled itself), interrupted (stopped by " +
            "the shutdown, it runs again)");

        private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>("xactjobs.job.duration", "s",
            "How long an attempt took, from its start until its outcome was saved");

        private static readonly Histogram<double> StartDelay = Meter.CreateHistogram<double>("xactjobs.job.start_delay", "s",
            "How long a job waited, from the time it was due (its scheduled time, or a waiting continuation's claim) until it started");

        private static readonly Histogram<double> ClaimDuration = Meter.CreateHistogram<double>("xactjobs.claim.duration", "s",
            "How long a worker's claim took, of continuations and due jobs");

        private static readonly Counter<long> Claimed = Meter.CreateCounter<long>("xactjobs.job.claimed", "{job}",
            "Jobs claimed (leased) by the workers");

        // the running workers, for the jobs they run and hold
        private static readonly ConcurrentDictionary<IXactJobsWorkerState, byte> Workers = new();

        static XactJobsMetrics()
        {
            Meter.CreateObservableUpDownCounter("xactjobs.job.running", () => Observe(w => w.RunningJobs), "{job}",
                "Jobs running now");

            Meter.CreateObservableUpDownCounter("xactjobs.job.held", () => Observe(w => w.HeldJobs), "{job}",
                "Jobs the workers hold (claimed, and not finished): running, and waiting for a slot");
        }

        private readonly bool _jobTags;

        /// <param name="jobTags">Also tags the attempts with the job's type and method (more series: one per job method).</param>
        public XactJobsMetrics(bool jobTags)
        {
            _jobTags = jobTags;
        }

        /// <summary>
        /// Counts the worker's jobs in the running and held jobs, until the result is disposed.
        /// </summary>
        public static IDisposable Register(IXactJobsWorkerState worker)
        {
            Workers.TryAdd(worker, 0);

            return new Registration(worker);
        }

        public static void RecordClaim(string queue, int jobs, TimeSpan duration)
        {
            var tags = new TagList { { QueueTag, queue } };

            ClaimDuration.Record(duration.TotalSeconds, tags);

            if (jobs > 0)
            {
                Claimed.Add(jobs, tags);
            }
        }

        public void RecordStartDelay(string queue, XactJob job, TimeSpan delay)
        {
            if (!StartDelay.Enabled) return;

            var tags = GetJobTags(queue, job);

            // the due time is the app's clock, when the job was scheduled: another node's can be a little ahead
            StartDelay.Record(Math.Max(delay.TotalSeconds, 0), tags);
        }

        public void RecordAttempt(string queue, XactJob job, string outcome, TimeSpan duration)
        {
            if (!Attempts.Enabled && !Duration.Enabled) return;

            var tags = GetJobTags(queue, job);

            tags.Add(OutcomeTag, outcome);

            Attempts.Add(1, tags);
            Duration.Record(duration.TotalSeconds, tags);
        }

        private TagList GetJobTags(string queue, XactJob job)
        {
            var tags = new TagList { { QueueTag, queue } };

            if (_jobTags)
            {
                tags.Add(JobTypeTag, job.TypeName);
                tags.Add(JobMethodTag, job.MethodName);
            }

            return tags;
        }

        // one measurement per queue: an observable instrument mustn't report the same tags twice
        private static IEnumerable<Measurement<int>> Observe(Func<IXactJobsWorkerState, int> value)
        {
            return Workers.Keys
                .GroupBy(w => w.QueueDisplayName)
                .Select(g => new Measurement<int>(g.Sum(value), new KeyValuePair<string, object?>(QueueTag, g.Key)));
        }

        private sealed class Registration(IXactJobsWorkerState worker) : IDisposable
        {
            public void Dispose() => Workers.TryRemove(worker, out _);
        }
    }

    /// <summary>
    /// What a worker reports to <see cref="XactJobsMetrics"/>.
    /// </summary>
    internal interface IXactJobsWorkerState
    {
        string QueueDisplayName { get; }

        int RunningJobs { get; }

        int HeldJobs { get; }
    }
}
