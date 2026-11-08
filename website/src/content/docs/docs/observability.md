---
title: Metrics and logs
description: The workers' metrics on the meter XactJobs, for OpenTelemetry or dotnet-counters, and the log scope of each job's attempt.
---

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
| `xactjobs.job.start_delay` | histogram | s | How long a job waited from the time it was due until it started: a queue falling behind shows here first. A continuation is due when it's claimed |
| `xactjobs.claim.duration` | histogram | s | How long a claim took |
| `xactjobs.job.claimed` | counter | `{job}` | Jobs claimed |
| `xactjobs.job.running` | up-down counter | `{job}` | Jobs running now |
| `xactjobs.job.held` | up-down counter | `{job}` | Jobs the workers hold: running, and waiting for a slot |

They're all tagged with `xactjobs.queue`. `options.WithJobTagsInMetrics()` also tags the attempts, their duration and the start delay
with `xactjobs.job.type` and `xactjobs.job.method`: one series per job method instead of per queue.

Each attempt runs in a log scope with the job's `JobId`, `JobType`, `JobMethod`, `JobQueue` and `JobAttempt`. What the job logs, and what
EF logs meanwhile, carries them, with a logging provider that records scopes (the console's `IncludeScopes`, Serilog, OpenTelemetry's
`IncludeScopes`).
