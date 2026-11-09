// Copyright (c) 2025 XactJobs
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;

namespace XactJobs.Internal
{
    /// <summary>
    /// The workers' traces, on the activity source "XactJobs": an activity (an OpenTelemetry span) for each attempt of a job, with the
    /// activities of what the job does inside it (EF's, HttpClient's), linked to the trace that enqueued the job. An app collects
    /// them with OpenTelemetry (<c>AddSource("XactJobs")</c>). Nothing is created while nothing listens.
    /// </summary>
    internal static class XactJobsTracing
    {
        public const string SourceName = "XactJobs";

        private static readonly ActivitySource Source = new(SourceName, typeof(XactJobsTracing).Assembly.GetName().Version?.ToString(3));

        // the activity's name per job method: its short type name and its method name, e.g. InvoiceJob.SendAsync
        private static readonly ConcurrentDictionary<(string TypeName, string MethodName), string> Names = new();

        /// <summary>
        /// The W3C traceparent of the current activity, stored with a job when it's enqueued, so that its attempts link to the trace
        /// that enqueued it. Null without an activity, or with one whose id isn't in the W3C format.
        /// </summary>
        public static string? GetCurrentTraceParent()
        {
            var current = Activity.Current;

            return current?.IdFormat == ActivityIdFormat.W3C ? current.Id : null;
        }

        /// <summary>
        /// Starts the activity of a job's attempt, a root linked to the trace that enqueued the job, if there's one. Null if nothing listens,
        /// or it isn't sampled.
        /// </summary>
        public static Activity? StartAttempt(XactJob job, string queue)
        {
            if (!Source.HasListeners()) return null;

            // a root, whatever runs around the worker: the job can run long after its enqueue, so that trace is linked, not the parent
            Activity.Current = null;

            ActivityLink[]? links = null;

            if (job.TraceParent != null && ActivityContext.TryParse(job.TraceParent, null, out var enqueuedBy))
            {
                links = [new ActivityLink(enqueuedBy)];
            }

            var activity = Source.StartActivity(GetName(job), ActivityKind.Consumer, parentContext: default, links: links);

            if (activity is { IsAllDataRequested: true })
            {
                // OpenTelemetry's messaging conventions, the closest there are to a job system's, and XactJobs' own
                activity.SetTag("messaging.system", "xactjobs");
                activity.SetTag("messaging.operation.type", "process");
                activity.SetTag("messaging.destination.name", queue);
                activity.SetTag("messaging.message.id", job.Id.ToString(CultureInfo.InvariantCulture));

                activity.SetTag("xactjobs.queue", queue);
                activity.SetTag("xactjobs.job.id", job.Id);
                activity.SetTag("xactjobs.job.type", job.TypeName);
                activity.SetTag("xactjobs.job.method", job.MethodName);
                activity.SetTag("xactjobs.attempt", job.ErrorCount + 1);

                if (job.BatchId != null) activity.SetTag("xactjobs.batch.id", job.BatchId.Value.ToString());
                if (job.PeriodicJobId != null) activity.SetTag("xactjobs.periodic_job.id", job.PeriodicJobId);
                if (job.OriginallyScheduledAt != null) activity.SetTag("xactjobs.originally_scheduled_at", job.OriginallyScheduledAt.Value.ToString("O", CultureInfo.InvariantCulture));
            }

            return activity;
        }

        /// <summary>
        /// The attempt's outcome (see <see cref="XactJobsMetrics"/>), and its exception: retried and failed are errors.
        /// </summary>
        public static void SetOutcome(Activity activity, string? outcome, Exception? exception)
        {
            if (!activity.IsAllDataRequested) return;

            if (outcome != null)
            {
                activity.SetTag("xactjobs.outcome", outcome);
            }

            if (exception != null)
            {
                activity.SetStatus(ActivityStatusCode.Error, exception.Message);

                // OpenTelemetry's exception event (Activity.AddException is .NET 9 and later)
                activity.AddEvent(new ActivityEvent("exception", tags: new ActivityTagsCollection
                {
                    { "exception.type", exception.GetType().FullName },
                    { "exception.message", exception.Message },
                    { "exception.stacktrace", exception.ToString() },
                }));
            }
        }

        private static string GetName(XactJob job)
        {
            return Names.GetOrAdd((job.TypeName, job.MethodName), static key =>
            {
                // "Namespace.Outer+Type, Assembly" and "SendAsync(System.String)": "Outer+Type.SendAsync". A generic type's arguments
                // ("Namespace.Type`1[[System.Int32, ...]], Assembly") are left out: "Type`1"
                var end = key.TypeName.IndexOfAny([',', '[']);
                var typeName = end >= 0 ? key.TypeName[..end] : key.TypeName;
                var shortType = typeName[(typeName.LastIndexOf('.') + 1)..];

                var parenthesis = key.MethodName.IndexOf('(');
                var method = parenthesis >= 0 ? key.MethodName[..parenthesis] : key.MethodName;

                return $"{shortType}.{method}";
            });
        }
    }
}
