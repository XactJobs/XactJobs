---
title: Queues and polling
description: Isolated queues with their own workers, how workers poll and claim batches of jobs, BatchSize, and QuickPoll.
---

## Isolated queues

XactJobs can run isolated workers for named queues. A job goes to a queue when it's enqueued:

```csharp
dbContext.JobEnqueue(() => Greeter.Say("Job 1"), QueueNames.Priority);
dbContext.JobEnqueue(() => Greeter.Say("Job 2"), QueueNames.LongRunning);

dbContext.SaveChanges();
```

Each queue has its own workers, configured at startup:

```csharp
builder.Services.AddXactJobs<UserDbContext>(options =>
{
    options.WithPriorityQueue();     // 2 workers, polling every 4 seconds
    options.WithLongRunningQueue(queue => queue.WithMaxDegreeOfParallelism(2)); // 2 workers, polling every 10 seconds
    options.WithIsolatedQueue("reports", queue => queue.WithWorkerCount(1));
});
```

| Queue | Name | Configured with |
|---|---|---|
| Default | `QueueNames.Default` (`*`) | the options themselves: 2 workers, polling every 6 seconds |
| Priority | `QueueNames.Priority` (`p`) | `WithPriorityQueue()`: 2 workers, polling every 4 seconds |
| Long-running | `QueueNames.LongRunning` (`l`) | `WithLongRunningQueue()`: 2 workers, polling every 10 seconds |
| Your own | any name | `WithIsolatedQueue(name)` |

An isolated queue has its own settings (workers, parallelism, batch size, polling, lease, shutdown timeout, retry strategy and periodic
jobs), set in its configure action. It doesn't take them from the default queue. Long-running jobs are best kept in their own queue (see below).

## Polling

A worker runs up to `MaxDegreeOfParallelism` jobs at a time, and holds a batch of claimed (leased) jobs: by default twice as many, so that
each slot has a job waiting for it. A free slot takes one of the jobs the worker holds, without waiting for a claim. A slow job only
occupies its own slot.

If a poll fills the batch there is likely a backlog, so the worker claims again once fewer jobs wait than it can run, instead of once for
each job that finishes: when the jobs it holds are done, or after a second if some are still running. Otherwise it waits for the polling
interval or a QuickPoll notification. Polling can stop only if all the slots are occupied, so long-running jobs are best kept in their own
queue.

The workers of a queue start at staggered times: with 2 workers polling every 6 seconds, the second one starts 3 seconds after the first.

While a job runs, its lease is extended automatically, every three quarters of `WithLeaseDuration` (default 120 seconds).

## Batch size

`WithBatchSize` changes the batch. A larger one saves claims, which helps short jobs: on PostgreSQL, 2 workers running 4 no-op jobs at a
time each drained a backlog about twice as fast with a batch of 100 as with no batch (see [Throughput](../throughput/)).

The cost is that the jobs a worker holds wait for its slots, also when another worker is idle, and wait for their leases to expire if the
worker crashes. At or below `MaxDegreeOfParallelism`, there's no batch: a worker only claims jobs it can start immediately, so they never
wait behind a slow job while another worker is free. When the worker stops, the jobs it holds that haven't started are released right
away, for the other workers.

```csharp
builder.Services.AddXactJobs<UserDbContext>(options =>
{
    options
        .WithWorkerCount(2)
        .WithMaxDegreeOfParallelism(4)
        .WithBatchSize(100);
});
```

## QuickPoll

QuickPoll can be used if a job should be executed immediately, without waiting for the next poll interval. `QuickPoll<TDbContext>` is
registered as a scoped service, so it can be injected in class constructors, just like the DbContext instances.

```csharp
public class YourWorkerOrController
{
    private readonly QuickPoll<UserDbContext> _quickPoll;
    private readonly UserDbContext _dbContext;

    public YourWorkerOrController(QuickPoll<UserDbContext> quickPoll)
    {
        _quickPoll = quickPoll;
        _dbContext = quickPoll.DbContext;
    }

    public async Task YourBusinessLogic()
    {
        // Your business logic uses _dbContext, as usual.

        // Enqueue the job through _quickPoll (instead of DbContext)
        _quickPoll.JobEnqueue(() => Greeter.Say("QuickPoll"));

        // Save changes through _quickPoll
        // (this calls DbContext.SaveChanges and notifies the workers)
        _quickPoll.SaveChangesAndNotify();
    }
}
```

- `SaveChangesAndNotifyAsync(cancellationToken)` is the async version.
- Inside a transaction, the workers are notified only when it commits: commit with `CommitAndNotify()` or `CommitAndNotifyAsync(cancellationToken)`.
- `JobBatch()` on QuickPoll registers the jobs of the batch for the notification.
- If you save or commit directly through the DbContext, call `Notify()` afterwards.

This can only work if the workers are running in the same process where the jobs are enqueued (which is the default).

A job enqueued this way is scheduled for the current time of the application. If that's still in the future for the database when the
worker polls (the clocks differ, or on SQLite within the same millisecond), the job runs at the next regular poll.
