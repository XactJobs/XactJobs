---
title: Periodic jobs
description: Recurring jobs on cron schedules, from attributes, the options or code, safe on many nodes, with state between runs, reconciliation, pause and resume.
---

Periodic (recurring) jobs run on a cron schedule. Mark the job methods with `[XactPeriodicJob]`: that's the simplest way, and they're
ensured at startup. [`WithPeriodicJob`](#in-the-options) in the options does the same for jobs an attribute can't describe, and
[`JobEnsurePeriodic`](#at-runtime) adds or changes them at runtime.

## Attributes

Mark the job methods with `[XactPeriodicJob]`, and add their assembly in the options:

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

- The id (`daily_report`) names the periodic job: the same on every node, and unique.
- The method is public, static or of a concrete class with a public constructor (the worker creates it, with the constructor's
  parameters from the services), not generic, and returns void or a Task.
- Its parameters can be a `CancellationToken`, an `XactJobContext`, and one string, which gets `Config` (null if it isn't set).
- `Queue` is the default queue if it isn't set, or an isolated queue, which must be configured.
- Attribute arguments must be constants: `CronExpressions` has the common schedules (`EverySecond`, `EveryMinute`, `Hourly`, `Daily`,
  `Weekly`, `Monthly`), and any [cron expression](#cron-expressions) can be written as a string.
- `AddXactJobs` checks them, and throws with all the problems it finds: a method that can't be a job, an invalid cron expression, an id
  used twice (also with `WithPeriodicJob`), a queue that isn't configured.

Removing an attribute doesn't delete its periodic job, whose runs keep running (and fail if the method is gone too): delete it by its
id instead, e.g. with `JobDeletePeriodicAsync` (see [Pause, resume and delete](#pause-resume-and-delete)), in place of the attribute.

## In the options

`WithPeriodicJob` defines a periodic job with a lambda, like the other jobs, e.g. for arguments that aren't constants, or a schedule from
the configuration:

```csharp
builder.Services.AddXactJobs<AppDbContext>(options =>
{
    options.WithPeriodicJob<ReportJobs>(
        x => x.ExportAsync("eu", CancellationToken.None),
        "export_eu",
        builder.Configuration["Reports:ExportSchedule"] ?? CronExpressions.Hourly);
});
```

An attribute is the same as a `WithPeriodicJob` with a call of the method, so a job can move from one to the other without changing
its definition. A periodic job defined on an isolated queue's options runs on that queue, e.g.
`options.WithLongRunningQueue(queue => queue.WithPeriodicJob(...))`.

## At runtime

`JobEnsurePeriodic` adds a periodic job, or updates it, from your code, followed by `SaveChanges`, e.g. for schedules your users
create:

```csharp
dbContext.JobEnsurePeriodic<ReportJobs>(
    x => x.ExportAsync("apac", CancellationToken.None),
    "export_apac",
    Cron.Daily(new TimeSpan(9, 30, 0)));

dbContext.SaveChanges();
```

Ensuring it again with another definition updates it. `JobEnsurePeriodicAsync` does the same with an async lookup, and both have an
overload with a queue before the cron expression. With several nodes, see [Several nodes](#several-nodes).

## Cron expressions

Cron expressions have 6 fields, starting with the seconds: `second minute hour day-of-month month day-of-week`, evaluated in UTC.
`CronExpressions` has constants for attributes, and the `Cron` class builds others:

| Helper | Expression |
|---|---|
| `Cron.SecondInterval(10)` | `*/10 * * * * *` |
| `Cron.MinuteInterval(5)` | `0 */5 * * * *` |
| `Cron.HourInterval(2)` | `0 0 */2 * * *` |
| `Cron.Daily()`, `CronExpressions.Daily` | `0 0 0 * * *`, every day at midnight UTC |
| `Cron.Daily(new TimeSpan(9, 30, 0))` | `0 30 9 * * *` |
| `Cron.Weekly(DayOfWeek.Monday)` | `0 0 0 * * 1` |
| `Cron.Weekly(DayOfWeek.Monday, new TimeSpan(8, 0, 0), DayOfWeek.Thursday)` | `0 0 8 * * 1,4` |

## Several nodes

Attributes and `WithPeriodicJob` are the safe way to define periodic jobs with several nodes starting at the same time, because the
nodes take turns under a lock. `JobEnsurePeriodic` can't take that lock, since the changes are only saved later by your `SaveChanges`.
If several nodes call it at the same time, it's still safe for an existing job whose definition didn't change, but for a new job, or a
changed definition (a new version), all of them except the first get a duplicate key error from `SaveChanges`.

## Reconciliation

Each run of a periodic job schedules the next one. Each node also checks every 5 minutes (`WithPeriodicJobReconciliationInterval`)
for active periodic jobs without a pending run, and schedules their next run. So a periodic job recovers if its pending row gets lost,
and rows added directly to the periodic job table are picked up. They need a valid cron expression, and the same type name, method name
and arguments format that XactJobs writes. The run happens at the next time on the cron schedule: missed runs are not caught up.

After a periodic job is deleted, or its definition changes, the pending run of the old definition stays in the table until its scheduled
time. A worker then skips it, adding a `Skipped` row to the history.

## One pending run

A periodic job has at most one pending run of each version: the job table has a unique index on the periodic job and its version.
If two pending runs are saved at the same time, e.g. two resumes of a paused job, or the dashboard's *Trigger* of a job without a pending
run while a node's reconciliation schedules it, only the first is saved: the other `SaveChanges` throws a `DbUpdateException`, for which
`ex.IsUniqueKeyViolation()` (namespace `XactJobs`) returns true. The dashboard answers it with a conflict, and the reconciliation runs
again.

## State between runs

A run can leave a short state for the next run of the same periodic job, e.g. a watermark or a cursor. It gets it in
`XactJobContext.PeriodicState`, and sets the next one with `SetPeriodicState`. The invoices and the state are saved together:

```csharp
public class InvoiceRun(AppDbContext db)
{
    [XactPeriodicJob("invoices", "0 */5 * * * *")]
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
```

- The state is saved with the run's own changes, in the same transaction, and only if the run completes. A failed attempt leaves it as
  it was, and its retry gets the same state.
- A run that doesn't call `SetPeriodicState` keeps the state, and `null` clears it.
- At most 2,000 characters (`XactJobPeriodic.MaxStateLength`). A longer one throws `ArgumentException`, which fails the attempt.
- It belongs to the periodic job's id: a new definition, pausing and resuming keep it, and deleting the periodic job deletes it.
  Attributes, `WithPeriodicJob` and `JobEnsurePeriodic` never change it. To change it outside a run, update the `state` column of the
  periodic job table.
- The history has each run's state, before and after the run.
- Only runs of periodic jobs have a state: in other jobs `PeriodicState` is null, and `SetPeriodicState` throws
  `InvalidOperationException`. The dashboard can't retry a run that had a state, because it would run as a one-off job, without it.

## Pause, resume and delete

Pausing and resuming are available in code, followed by `SaveChanges`:

```csharp
await dbContext.JobSetPeriodicActiveAsync("daily_report", false, cancellationToken); // pause
await dbContext.SaveChangesAsync(cancellationToken);
```

- Pausing removes the pending runs that are not running at the moment, so a paused job doesn't fill the history with skipped runs. A run
  in progress finishes, and doesn't schedule the next one if the job is still paused by then.
- Resuming schedules the next run, unless there already is one.
- A paused job stays paused when the application starts again, even if an attribute, `WithPeriodicJob` or `JobEnsurePeriodic` defines it.
- `JobDeletePeriodicAsync(id, cancellationToken)` deletes the definition, also followed by `SaveChanges`. A periodic job defined with an
  attribute or `WithPeriodicJob` is added again when the application starts.

Both return `false` if the periodic job doesn't exist. The [dashboard](../dashboard/) has the same actions, and *Trigger*.
