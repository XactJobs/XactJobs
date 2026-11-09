---
title: Metrics, traces and logs
description: The workers' metrics on the meter XactJobs, an activity per job attempt linked to the trace that enqueued it, and the log scope of each attempt.
---

## Metrics

The workers publish metrics on the meter `XactJobs` (`System.Diagnostics.Metrics`, part of .NET), so XactJobs needs no OpenTelemetry
package. Collect them with OpenTelemetry, `dotnet-counters` or a `MeterListener`. Nothing is recorded while nothing listens. With the
OpenTelemetry packages:

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics.AddMeter("XactJobs"));
```

| Metric | Type | Unit | Description |
|---|---|---|---|
| `xactjobs.job.attempts` | counter | `{attempt}` | Attempts that ended, by `xactjobs.outcome`: `completed`; `skipped` (an inactive periodic job, or a continuation whose condition isn't met); `retried` (failed, and scheduled again); `failed` (out of retries, or cancelled itself); `interrupted` (stopped by the shutdown, it runs again) |
| `xactjobs.job.duration` | histogram | s | How long an attempt took, until its outcome was saved, by `xactjobs.outcome` |
| `xactjobs.job.start_delay` | histogram | s | How long a job waited from the time it was due until it started: a queue falling behind shows here first. A continuation is due when it's picked up |
| `xactjobs.claim.duration` | histogram | s | How long a worker's query for due jobs took |
| `xactjobs.job.claimed` | counter | `{job}` | Jobs the workers picked up |
| `xactjobs.job.running` | up-down counter | `{job}` | Jobs running now |
| `xactjobs.job.held` | up-down counter | `{job}` | Jobs the workers hold: running, and waiting for a slot |

They're all tagged with `xactjobs.queue`. `options.WithJobTagsInMetrics()` also tags the attempts, their duration and the start delay
with `xactjobs.job.type` and `xactjobs.job.method`: one series per job method instead of per queue.

## Traces

Each attempt of a job is also an activity (an OpenTelemetry span) of the activity source `XactJobs`. What the job does runs inside it, so
EF's and HttpClient's activities, and your own, show up under the job. Collect them with OpenTelemetry:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddSource("XactJobs"));
```

- The activity is named after the job's type and method, e.g. `InvoiceJob.SendAsync`, and its kind is `Consumer`. Its tags follow
  OpenTelemetry's messaging conventions (`messaging.system` = `xactjobs`, `messaging.operation.type` = `process`,
  `messaging.destination.name` = the queue, `messaging.message.id` = the job's id), with `xactjobs.queue`, `xactjobs.job.id`,
  `xactjobs.job.type`, `xactjobs.job.method`, `xactjobs.attempt` and `xactjobs.outcome`, and `xactjobs.batch.id`,
  `xactjobs.periodic_job.id` and `xactjobs.originally_scheduled_at` when the job has them.
- A failed attempt (`retried` or `failed`) has the status `Error`, with the exception as an `exception` event.
- Linked to the trace that enqueued the job: a job enqueued while an activity runs, e.g. in a request, stores its W3C `traceparent`,
  and each of its attempts links to that trace. It's a link, not the parent: a job can run long after the request, and more than once.
  Retries keep it, and the history and the dashboard's Run now and Retry too. The runs of periodic jobs have none.
- Nothing is created while nothing listens.

## Logs

Each attempt runs in a log scope with the job's `JobId`, `JobType`, `JobMethod`, `JobQueue` and `JobAttempt`. What the job logs, and what
EF logs meanwhile, carries them, with a logging provider that records scopes (the console's `IncludeScopes`, Serilog, OpenTelemetry's
`IncludeScopes`).
