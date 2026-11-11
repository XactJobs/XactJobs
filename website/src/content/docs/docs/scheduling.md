---
title: Scheduling
description: Fire-and-forget, delayed and periodic jobs, several jobs in one transaction, and periodic jobs on many nodes, with reconciliation, pause and resume.
---

The examples use this simple job:

```csharp
public static class Greeter
{
    public static void Say(string message) => Console.WriteLine(message);
}
```

Every method below only adds rows to the `DbContext`: nothing is scheduled until your `SaveChanges` commits.

## Fire and forget

Fire-and-forget jobs are executed once by a background worker, in its next poll. Each worker polls every 6 seconds by default
(`WithPollingInterval`), and the workers of a queue are started at staggered times, so the default 2 workers take turns every 3 seconds.
[QuickPoll](../queues-and-polling/#quickpoll) starts a job without waiting for the next poll.

```csharp
// ... other business logic
dbContext.JobEnqueue(
    () => Greeter.Say("Fire-and-forget!"));

dbContext.SaveChanges();
```

## Delayed jobs

Delayed jobs are executed once after the specified time interval, or at the specified time:

```csharp
dbContext.JobScheduleIn(
    () => Greeter.Say("Delayed!"),
    TimeSpan.FromDays(7));

dbContext.JobScheduleAt(
    () => Greeter.Say("At a time!"),
    new DateTime(2027, 1, 1, 9, 0, 0, DateTimeKind.Utc));

dbContext.SaveChanges();
```

`JobEnqueue`, `JobScheduleIn` and `JobScheduleAt` also take a queue as their last argument: see [Queues](../queues-and-polling/#isolated-queues).

## Several jobs at once

Adding multiple jobs before saving changes enqueues all of them in a single transaction:

```csharp
dbContext.JobEnqueue(() => Greeter.Say("Job 1"));
dbContext.JobEnqueue(() => Greeter.Say("Job 2"));

dbContext.SaveChanges();
```

## Transactions

Jobs participate in DbContext transactions, just like the regular entities in EF Core:

```csharp
using var tx = dbContext.Database.BeginTransaction();

// some business logic
dbContext.JobEnqueue(() => Greeter.Say("Job 1"));
dbContext.SaveChanges();

// some more business logic
dbContext.JobEnqueue(() => Greeter.Say("Job 2"));
dbContext.SaveChanges();

tx.Commit();
```

If the transaction rolls back, neither the business data nor the jobs exist.

## Periodic jobs

Periodic (recurring) jobs run according to a cron schedule:

```csharp
dbContext.JobEnsurePeriodic(
    () => Greeter.Say("Recurring!"),
    "my_recurring_job",
    Cron.Daily());

dbContext.SaveChanges();
```

The id (`my_recurring_job`) names the periodic job: ensuring it again with another definition updates it. `JobEnsurePeriodicAsync`
does the same with an async lookup, and both have an overload with a queue before the cron expression.

If the periodic jobs are known at startup, define them in the options instead:

```csharp
builder.Services.AddXactJobs<UserDbContext>(options =>
{
    options.WithPeriodicJob(
        () => Greeter.Say("Recurring!"),
        "my_recurring_job",
        Cron.Daily());
});
```

A periodic job defined on an isolated queue's options runs on that queue, e.g.
`options.WithLongRunningQueue(queue => queue.WithPeriodicJob(...))`.

### Attributes

Or mark the job methods with `[XactPeriodicJob]`, and add their assembly in the options:

```csharp
public class ReportJobs
{
    [XactPeriodicJob("daily_report", CronExpressions.Daily)]
    public Task SendDailyAsync(CancellationToken cancellationToken) => Task.CompletedTask; // the report

    // one method, two periodic jobs: each one's config is passed to the string parameter
    [XactPeriodicJob("export_eu", CronExpressions.Hourly, Config = "eu")]
    [XactPeriodicJob("export_us", CronExpressions.Hourly, Config = "us", Queue = QueueNames.LongRunning)]
    public Task ExportAsync(string region, CancellationToken cancellationToken) => Task.CompletedTask; // the export of the region
}

builder.Services.AddXactJobs<AppDbContext>(options =>
{
    options
        .WithLongRunningQueue()
        .WithPeriodicJobsFromAssemblyOf<ReportJobs>(); // or WithPeriodicJobsFromAssembly(assembly)
});
```

Each attribute is the same as a `WithPeriodicJob` with a call of the method, so a job can move from one to the other without
changing its definition. The method is public, static or of a concrete class with a public constructor, not generic, and returns
void or a Task. Its parameters can be a `CancellationToken`, an `XactJobContext`, and one string for the config. Attribute arguments
must be constants: `CronExpressions` has the common schedules (`EverySecond`, `EveryMinute`, `Hourly`, `Daily`, `Weekly`, `Monthly`).
`AddXactJobs` checks them, and throws with all the problems it finds: a method that can't be a job, an invalid cron expression, an id
used twice (also with `WithPeriodicJob`), a queue that isn't configured.

Removing an attribute doesn't delete its periodic job, whose runs keep running (and fail if the method is gone too): delete it by its
id instead, e.g. with `JobDeletePeriodicAsync`, in place of the attribute.

### Cron expressions

Cron expressions have 6 fields, starting with the seconds: `second minute hour day-of-month month day-of-week`, evaluated in UTC.
The `Cron` class builds the common ones:

| Helper | Expression |
|---|---|
| `Cron.SecondInterval(10)` | `*/10 * * * * *` |
| `Cron.MinuteInterval(5)` | `0 */5 * * * *` |
| `Cron.HourInterval(2)` | `0 0 */2 * * *` |
| `Cron.Daily()` | `0 0 0 * * *`, every day at midnight UTC |
| `Cron.Daily(new TimeSpan(9, 30, 0))` | `0 30 9 * * *` |
| `Cron.Weekly(DayOfWeek.Monday)` | `0 0 0 * * 1` |
| `Cron.Weekly(DayOfWeek.Monday, new TimeSpan(8, 0, 0), DayOfWeek.Thursday)` | `0 0 8 * * 1,4` |

### Several nodes

Defining periodic jobs in the options, or with attributes, is the safe way to do it with several nodes starting at the same time, because the nodes take
turns under a lock. `JobEnsurePeriodic` can't take that lock, since the changes are only saved later by your `SaveChanges`. If several nodes call it
at the same time, it's still safe for an existing job whose definition didn't change, but for a new job, or a changed definition (a
new version), all of them except the first get a duplicate key error from `SaveChanges`.

### Reconciliation

Each run of a periodic job schedules the next one. Each node also checks every 5 minutes (`WithPeriodicJobReconciliationInterval`)
for active periodic jobs without a pending run, and schedules their next run. So a periodic job recovers if its pending row gets lost,
and rows added directly to the periodic job table are picked up. They need a valid cron expression, and the same type name, method name
and arguments format that XactJobs writes. The run happens at the next time on the cron schedule: missed runs are not caught up.

After a periodic job is deleted, or its definition changes, the pending run of the old definition stays in the table until its scheduled
time. A worker then skips it, adding a `Skipped` row to the history.

### One pending run

A periodic job has at most one pending run of each version: the job table has a unique index on the periodic job and its version.
If two pending runs are saved at the same time, e.g. two resumes of a paused job, or the dashboard's *Trigger* of a job without a pending
run while a node's reconciliation schedules it, only the first is saved: the other `SaveChanges` throws a `DbUpdateException`, for which
`ex.IsUniqueKeyViolation()` (namespace `XactJobs`) returns true. The dashboard answers it with a conflict, and the reconciliation runs
again.

### State between runs

A run can leave a short state for the next run of the same periodic job, e.g. a watermark or a cursor. It gets it in
`XactJobContext.PeriodicState`, and sets the next one with `SetPeriodicState`. The invoices and the state are saved together:

```csharp
public class InvoiceRun(AppDbContext db)
{
    public async Task CreateInvoicesAsync(XactJobContext context, CancellationToken cancellationToken)
    {
        // the last order the previous runs invoiced
        var lastOrderId = long.Parse(context.PeriodicState ?? "0");

        var orders = await db.Orders
            .Where(o => o.Id > lastOrderId)
            .OrderBy(o => o.Id)
            .Take(500)
            .ToListAsync(cancellationToken);

        if (orders.Count == 0) return;

        db.Invoices.AddRange(orders.Select(Invoice.For));

        context.SetPeriodicState(orders[^1].Id.ToString());
    }
}

dbContext.JobEnsurePeriodic<InvoiceRun>(
    x => x.CreateInvoicesAsync(null!, CancellationToken.None),
    "invoices",
    Cron.MinuteInterval(5));
```

- The state is saved with the run's own changes, in the same transaction, and only if the run completes. A failed attempt leaves it as
  it was, and its retry gets the same state.
- A run that doesn't call `SetPeriodicState` keeps the state, and `null` clears it.
- At most 2,000 characters (`XactJobPeriodic.MaxStateLength`). A longer one throws `ArgumentException`, which fails the attempt.
- It belongs to the periodic job's id: a new definition, pausing and resuming keep it, and deleting the periodic job deletes it.
  `JobEnsurePeriodic` and `WithPeriodicJob` never change it. To change it outside a run, update the `state` column of the periodic job
  table.
- The history has each run's state, before and after the run.
- Only runs of periodic jobs have a state: in other jobs `PeriodicState` is null, and `SetPeriodicState` throws
  `InvalidOperationException`. The dashboard can't retry a run that had a state, because it would run as a one-off job, without it.

### Pause, resume and delete

Pausing and resuming are available in code, followed by `SaveChanges`:

```csharp
await dbContext.JobSetPeriodicActiveAsync("my_recurring_job", false, cancellationToken); // pause
await dbContext.SaveChangesAsync(cancellationToken);
```

- Pausing removes the pending runs that are not running at the moment, so a paused job doesn't fill the history with skipped runs. A run
  in progress finishes, and doesn't schedule the next one if the job is still paused by then.
- Resuming schedules the next run, unless there already is one.
- A paused job stays paused when the application starts again, even if `JobEnsurePeriodic` or `WithPeriodicJob` defines it.
- `JobDeletePeriodicAsync(id, cancellationToken)` deletes the definition, also followed by `SaveChanges`. A periodic job defined in code
  is added again when the application starts.

Both return `false` if the periodic job doesn't exist. The [dashboard](../dashboard/) has the same actions, and *Trigger*.
