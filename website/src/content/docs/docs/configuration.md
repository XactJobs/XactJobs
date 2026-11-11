---
title: Configuration
description: Every option of AddXactJobs, the queues, the model configuration and the dashboard, with its default.
---

```csharp
builder.Services.AddXactJobs<UserDbContext>(options =>
{
    options
        .WithWorkerCount(2)                         // workers polling the queue
        .WithMaxDegreeOfParallelism(8)              // jobs running at the same time, per worker (default: processor count)
     // .WithBatchSize(16)                          // jobs a worker picks up and holds at a time (default: twice MaxDegreeOfParallelism)
        .WithPollingInterval(6)                     // seconds between polls, per worker
        .WithLeaseDuration(120)                     // seconds, extended automatically while a job is running
        .WithShutdownTimeout(10)                    // seconds running jobs get to finish on stop, before they are cancelled
        .WithPeriodicJobReconciliationInterval(300) // seconds between the checks for periodic jobs without a pending run
        .WithSaveOutsideTransaction(XactJobSaveCheck.Warn); // when a job saves without a transaction: Warn, Throw or Ignore (all the queues)
     // .WithHistoryRetentionPeriodInDays(30)       // deletes the older history every hour (default: the history is kept)
     // .WithJobTagsInMetrics()                     // tags the jobs' metrics with the job's type and method (all the queues)

    // isolated queues have their own settings
    options.WithLongRunningQueue(queue => queue.WithMaxDegreeOfParallelism(2));
});
```

The values above are the defaults, except for `WithMaxDegreeOfParallelism`.

## Each queue

These are set on the options for the default queue, and in the configure action of each isolated queue. An isolated queue starts from
these defaults, not from the default queue's settings.

| Option | Default | Description |
|---|---|---|
| `WithWorkerCount(n)` | 2 | Workers polling the queue, started at staggered times |
| `WithMaxDegreeOfParallelism(n)` | processor count | Jobs running at the same time, per worker |
| `WithBatchSize(n)` | twice `MaxDegreeOfParallelism` | Jobs a worker picks up and holds at a time. At or below `MaxDegreeOfParallelism`, no batch: see [Batch size](../queues-and-polling/#batch-size) |
| `WithPollingInterval(seconds)` | 6 | Seconds between polls, per worker |
| `WithLeaseDuration(seconds)` | 120 | How long a job a worker picked up stays reserved for it (leased), extended every three quarters of it while the job runs |
| `WithShutdownTimeout(seconds)` | 10 | Seconds running jobs get to finish on stop, before they are cancelled. 0 cancels them immediately |
| `ClearLeaseTimeoutInSeconds` | 10 | How long the workers wait to clear the leases on stop. No builder method: set `options.Options.ClearLeaseTimeoutInSeconds` |
| `WithRetryStrategy(strategy)` | `DefaultRetryStrategy` | When a failed job runs again: see [Failures and retries](../failures-and-retries/) |
| `WithPeriodicJob(job, id, cron)` | none | A periodic job ensured at startup, on this queue: see [Periodic jobs](../periodic-jobs/#in-the-options) |

## All the queues

| Option | Default | Description |
|---|---|---|
| `WithHistoryRetentionPeriodInDays(days)` | none: the history is kept | Adds the periodic job `xj_history_cleanup`, which deletes the older history every hour, except the history of the batches that continuations still wait for |
| `WithPeriodicJobReconciliationInterval(seconds)` | 300 | Seconds between each node's checks for active periodic jobs without a pending run |
| `WithPeriodicJobsFromAssembly(assembly)`, `WithPeriodicJobsFromAssemblyOf<T>()` | none | The periodic jobs of the `[XactPeriodicJob]` methods in the assembly, ensured at startup: see [Periodic jobs](../periodic-jobs/#attributes) |
| `WithSaveOutsideTransaction(check)` | `XactJobSaveCheck.Warn` | When a job saves without a transaction: `Warn`, `Throw` or `Ignore`. See [the save check](../unit-of-work/#the-save-check) |
| `WithJobTagsInMetrics(enabled?)` | off | Also tags the jobs' metrics with the job's type and method. See [Metrics, traces and logs](../observability/) |
| `WithPriorityQueue(configure?)` | not started | Workers for `QueueNames.Priority`: 2, polling every 4 seconds |
| `WithLongRunningQueue(configure?)` | not started | Workers for `QueueNames.LongRunning`: 2, polling every 10 seconds |
| `WithIsolatedQueue(name, configure?)` | none | Workers for a queue of your own |

By default the history is kept, and XactJobs deletes nothing. Set `WithHistoryRetentionPeriodInDays` the same on every node: a node without
it removes the cleanup job when it starts. Versions before 0.3.0 always added it, with 30 days: to keep that, call
`WithHistoryRetentionPeriodInDays(30)`.

## The model

```csharp
modelBuilder.ApplyXactJobsConfigurations(Database.ProviderName, excludeFromMigrations: true, schema: null);
```

| Parameter | Default | Description |
|---|---|---|
| `providerName` | | `Database.ProviderName`: the entity configurations depend on the database |
| `excludeFromMigrations` | `true` | `false` includes the XactJobs tables in your EF migrations |
| `schema` | `xact_jobs` on PostgreSQL, `XactJobs` on SQL Server | The schema of XactJobs' tables, for [several apps in one database](../databases/#several-apps-in-one-database). PostgreSQL and SQL Server only |

## Dashboard

`AddXactJobsUI<TDbContext>(options => ...)`, from the `XactJobs.UI` package:

| Option | Default | Description |
|---|---|---|
| `WithBasePath(path)` | `/XactJobs` | The dashboard's pages |
| `WithApiBasePath(path)` | `/api/XactJobs` | The REST API |
| `WithPageSize(n)` | 20 | Rows per page in the lists |
| `WithDashboardTitle(title)` | `XactJobs Dashboard` | The title shown in the dashboard |
| `WithRefreshInterval(seconds)` | 5 | How often the pages refresh, at least 5 |
| `AllowLocalRequestsOnly()` | the default | Only requests from the local machine |
| `RequireAuthorization()` | | Any signed-in user |
| `RequireAuthorizationPolicy(name)` | | Users that satisfy the policy |
| `AllowAnonymous()` | | Anyone: only for development, or when the access is restricted in another way |
| `DisableAntiforgery()` | antiforgery on | The API's actions don't require an antiforgery token, for clients that don't use cookies |
