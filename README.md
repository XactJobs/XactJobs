# XactJobs
XactJobs lets you schedule and run background jobs **with full database transaction support** using EntityFrameworkCore. 
Jobs are saved alongside your business data and only execute if your transaction commits - no more “ghost” jobs on rollback.

Supports **SQL Server, PostgreSQL, MySQL, Oracle and SQLite**, on .NET 8, 9 and 10.

XactJobs uses EF Core 9 on .NET 8 and 9, and EF Core 10 on .NET 10, so your database provider has to support that version. For MySQL, see [which provider to use](#mysql).

## A Job Is a Unit of Work
**The worker saves a job's database changes, not the job.** Changes a job makes through the injected `TDbContext` are saved when the job finishes, in the same transaction that marks it as completed:

- **Exactly once.** If a lease expires and another worker runs the job again, only one of the runs can commit, so the changes happen once.
- **No transaction stays open while the job runs**, unless the job opens one. The changes wait in EF's change tracker, and are written at the end in one short transaction.
- **Don't call `SaveChanges` in a job**, or call it only inside a transaction you open and don't commit: the worker commits it. Changes saved any other way are permanent right away, even if the job then fails.
- **Returning saves.** Returning normally, even early, saves the pending changes and records the job as completed. A job that wants to stop without saving must throw instead.
- **Throwing discards.** Any exception discards the pending changes, and the job is retried. To stop a job that can never succeed, e.g. because the data it works on is gone, throw `XactJobCancelException`: its changes are discarded too, but it isn't retried, and it moves to the history as cancelled, with the exception's message.

```csharp
public class InvoiceJob(UserDbContext db)
{
    public async Task CreateInvoiceAsync(int orderId)
    {
        var order = await db.Orders.FindAsync(orderId)
            ?? throw new XactJobCancelException($"Order {orderId} doesn't exist anymore");

        db.Invoices.Add(new Invoice(order.Id)); // no SaveChanges: the worker saves it when the job finishes
    }
}
```

The details are in [Exactly-Once Database Effects](#exactly-once-database-effects), and for `XactJobCancelException` in [Failures and Retries](#failures-and-retries).

## Status
XactJobs is pre-1.0. The core is tested on all five databases, with an integration test suite running against each one (SQLite in-process, the others in containers): transactional enqueue, claiming and leasing, retries, batches and continuations, and periodic jobs.

Until 1.0:
- **The API can still change** between minor versions (0.x). Breaking changes are listed in the release notes, with how to migrate.
- **Schema changes ship as SQL scripts**, one per version and database, with [upgrade notes](#installation) in this README. Existing data is always kept.
- **1.0 freezes the API and the schema** under semantic versioning.

Feedback and issue reports are very welcome, especially from production use.

## Features
The examples below use this simple job:

```csharp
public static class Greeter
{
    public static void Say(string message) => Console.WriteLine(message);
}
```

### Fire and Forget Jobs
Fire-and-forget jobs are executed once by a background worker in the next polling interval (configurable, default 3s).

```csharp
// ... other business logic
dbContext.JobEnqueue(
    () => Greeter.Say("Fire-and-forget!"));

dbContext.SaveChanges();
```

### Delayed Jobs
Delayed jobs are executed once after the specified time interval.

```csharp
dbContext.JobScheduleIn(
    () => Greeter.Say("Delayed!"),
    TimeSpan.FromDays(7));

dbContext.SaveChanges();
```

### Recurring jobs
Recurring jobs run periodically according to the specified cron schedule.

```csharp
dbContext.JobEnsurePeriodic(
    () => Greeter.Say("Recurring!"),
    "my_recurring_job",
    Cron.Daily());

dbContext.SaveChanges();
```

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

This is the safe way to do it with several nodes starting at the same time, because the nodes take turns under a lock.
`JobEnsurePeriodic` can't take that lock, since the changes are only saved later by your `SaveChanges`. If several nodes
call it at the same time, it's still safe for an existing job (only the definition saved last runs), but for a new job all of them
except the first get a duplicate key error from `SaveChanges`.

Each run of a periodic job schedules the next one. Each node also checks every 5 minutes (`WithPeriodicJobReconciliationInterval`)
for active periodic jobs without a pending run, and schedules their next run. So a periodic job recovers if its pending
row gets lost, and rows added directly to the periodic job table are picked up. They need a valid cron expression, and the same
type name, method name and arguments format that XactJobs writes. The run happens at the next time on the cron schedule,
missed runs are not caught up.

After a periodic job is deleted, or its definition changes, the pending run of the old definition stays in the table until its
scheduled time. A worker then skips it, adding a `Skipped` row to the history.

### Several Jobs at Once
Adding multiple jobs before saving changes will enqueue all jobs in a single transaction.

```csharp
dbContext.JobEnqueue(() => Greeter.Say("Job 1"));
dbContext.JobEnqueue(() => Greeter.Say("Job 2"));

dbContext.SaveChanges();
```

### Continuations
A continuation is a job that starts when another job is finished. It can have continuations of its own:

```csharp
dbContext.JobEnqueue<Importer>(x => x.ImportAsync(fileId))
    .ContinueWith<Reports>(x => x.RebuildAsync())
    .ContinueWith<Mailer>(x => x.SendReportAsync());

dbContext.SaveChanges();
```

By default a continuation runs if the job succeeded. The condition can also be `OnFailure` or `OnFinish`, and a continuation can go to another queue:

```csharp
var import = dbContext.JobEnqueue<Importer>(x => x.ImportAsync(fileId));

import.ContinueWith<Reports>(x => x.RebuildAsync());
import.ContinueWith<Ops>(x => x.AlertAsync(fileId), XactJobContinuation.OnFailure, QueueNames.Priority);
```

| Condition | Runs when |
|---|---|
| `OnSuccess` (default) | all the jobs it waits for succeeded |
| `OnFailure` | at least one of them failed |
| `OnFinish` | they are all finished, whether they succeeded or not |

A job failed if it ran out of retries, threw `XactJobCancelException`, was cancelled from the dashboard, or was a continuation that was skipped,
so in a chain of `OnSuccess` continuations, a failure skips the rest of the chain. A continuation whose condition isn't met moves to the history as `Skipped`,
with the reason as its message. Attempts that were retried don't count: a job of a batch that succeeds on its second attempt succeeded.

A job can only be continued before it's saved, because its row can't be changed afterwards. To continue jobs that are saved separately, put them in a batch.

### Batches
A batch is a group of jobs whose continuations start when all of them are finished:

```csharp
var batch = dbContext.JobBatch()
    .Enqueue<InventorySync>(x => x.SyncAsync("leeds"))
    .Enqueue<InventorySync>(x => x.SyncAsync("reno"))
    .ScheduleIn<InventorySync>(x => x.SyncAsync("osaka"), TimeSpan.FromHours(2));

batch.ContinueWith<Reports>(x => x.RebuildAsync())
    .ContinueWith<Mailer>(x => x.SendReportAsync());

batch.ContinueWith<Ops>(x => x.AlertAsync(), XactJobContinuation.OnFailure);

dbContext.SaveChanges();
```

The jobs of a batch are saved together, with one `SaveChanges`: once it's saved, a batch can't get more jobs. Continuations can be added at any time, also later by
the batch's id (`batch.Id`). If the batch is already finished, they start right away:

```csharp
dbContext.JobBatch(batchId)
    .ContinueWith<Audit>(x => x.LogAsync(batchId), XactJobContinuation.OnFinish);

dbContext.SaveChanges();
```

A batch id that doesn't exist is a finished batch without jobs, so its continuations run as if all of its jobs succeeded.

- A retry of a job stays in its batch, and a continuation waits for it. A job retried from the dashboard is a new job, outside of the batch.
- A job of a batch can't be continued on its own: continue the batch. Periodic jobs can't be in a batch, nor be continued.
- A skipped continuation can be retried from the dashboard: it runs as a new job, outside of any batch.
- The worker that finishes the last job of a batch wakes up the workers of the continuations' queues, if they run in the same process,
  and the continuations start as soon as a worker is free. Besides that, each worker checks for continuations of finished batches once per polling interval.
  The check is one query, which looks at every continuation of the queue that's still waiting, so keep it in mind with many thousands of them.
- A continuation that waits for its batch is stored with the scheduled time 9999-12-31, so the poll for due jobs never reaches it.
- With QuickPoll, `JobBatch()` registers the jobs of the batch for the notification. Their continuations don't need it.
- The history cleanup keeps the history of the batches that continuations still wait for, since it tells if their jobs failed.

### Transactions
Jobs participate in DbContext transactions, just like the regular entities in EFCore.

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
### Isolated Queues
XactJobs can be configured to run isolated workers for named queues.
```csharp
dbContext.JobEnqueue(() => Greeter.Say("Job 1"), QueueNames.Priority);
dbContext.JobEnqueue(() => Greeter.Say("Job 2"), QueueNames.LongRunning);

dbContext.SaveChanges();
```

### QuickPoll
QuickPoll can be used if a job should be executed immediately, without waiting for the next poll interval.
QuickPoll instance is registered as a scoped service so it can be injected in class constructors, just like the DbContext instances.

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
This can only work if the workers are running in the same process where the jobs are enqueued (which is the default).

A job enqueued this way is scheduled for the current time of the application. If that's still in the future for the database when
the worker polls (the clocks differ, or on SQLite within the same millisecond), the job runs at the next regular poll.

# Installation

Install the XactJobs nuget package:  
```
dotnet add package XactJobs
```

Add Entity Configurations to your DbContext:
```csharp
public class UserDbContext: DbContext
{
    // Your DbSets...

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyXactJobsConfigurations(Database.ProviderName);

        // Apply your entity configurations...
    }
}
```

Register XactJobs with your service provider
```csharp
// This registers 2 workers for the default queue
builder.Services.AddXactJobs<UserDbContext>(options =>
{
    options.WithPriorityQueue();     // optional (2 additional workers)
    options.WithLongRunningQueue();  // optional (2 additional workers)
});
```

Finally create the XactJobs tables in your database, by running all the SQL scripts for your database in the [sql](https://github.com/XactJobs/XactJobs/tree/main/sql) folder, in version order:

| Database | Scripts |
|---|---|
| SQL Server | [v0.1.0](https://github.com/XactJobs/XactJobs/blob/main/sql/v0.1.0/xact-jobs-sqlserver-v0.1.0.sql), [v0.1.4](https://github.com/XactJobs/XactJobs/blob/main/sql/v0.1.4/xact-jobs-sqlserver-v0.1.4.sql), [v0.1.5](https://github.com/XactJobs/XactJobs/blob/main/sql/v0.1.5/xact-jobs-sqlserver-v0.1.5.sql), [v0.2.0](https://github.com/XactJobs/XactJobs/blob/main/sql/v0.2.0/xact-jobs-sqlserver-v0.2.0.sql), [v0.3.0](https://github.com/XactJobs/XactJobs/blob/main/sql/v0.3.0/xact-jobs-sqlserver-v0.3.0.sql) |
| PostgreSQL | [v0.1.0](https://github.com/XactJobs/XactJobs/blob/main/sql/v0.1.0/xact-jobs-postgresql-v0.1.0.sql), [v0.1.4](https://github.com/XactJobs/XactJobs/blob/main/sql/v0.1.4/xact-jobs-postgresql-v0.1.4.sql), [v0.1.5](https://github.com/XactJobs/XactJobs/blob/main/sql/v0.1.5/xact-jobs-postgresql-v0.1.5.sql), [v0.2.0](https://github.com/XactJobs/XactJobs/blob/main/sql/v0.2.0/xact-jobs-postgresql-v0.2.0.sql), [v0.3.0](https://github.com/XactJobs/XactJobs/blob/main/sql/v0.3.0/xact-jobs-postgresql-v0.3.0.sql) |
| MySQL | [v0.1.0](https://github.com/XactJobs/XactJobs/blob/main/sql/v0.1.0/xact-jobs-mysql-v0.1.0.sql), [v0.1.4](https://github.com/XactJobs/XactJobs/blob/main/sql/v0.1.4/xact-jobs-mysql-v0.1.4.sql), [v0.1.5](https://github.com/XactJobs/XactJobs/blob/main/sql/v0.1.5/xact-jobs-mysql-v0.1.5.sql), [v0.2.0](https://github.com/XactJobs/XactJobs/blob/main/sql/v0.2.0/xact-jobs-mysql-v0.2.0.sql), [v0.3.0](https://github.com/XactJobs/XactJobs/blob/main/sql/v0.3.0/xact-jobs-mysql-v0.3.0.sql) |
| Oracle | [v0.1.0](https://github.com/XactJobs/XactJobs/blob/main/sql/v0.1.0/xact-jobs-oracle-v0.1.0.sql), [v0.1.4](https://github.com/XactJobs/XactJobs/blob/main/sql/v0.1.4/xact-jobs-oracle-v0.1.4.sql), [v0.1.5](https://github.com/XactJobs/XactJobs/blob/main/sql/v0.1.5/xact-jobs-oracle-v0.1.5.sql), [v0.2.0](https://github.com/XactJobs/XactJobs/blob/main/sql/v0.2.0/xact-jobs-oracle-v0.2.0.sql), [v0.3.0](https://github.com/XactJobs/XactJobs/blob/main/sql/v0.3.0/xact-jobs-oracle-v0.3.0.sql) |
| SQLite | [v0.1.0](https://github.com/XactJobs/XactJobs/blob/main/sql/v0.1.0/xact-jobs-sqlite-v0.1.0.sql), [v0.1.4](https://github.com/XactJobs/XactJobs/blob/main/sql/v0.1.4/xact-jobs-sqlite-v0.1.4.sql), [v0.1.5](https://github.com/XactJobs/XactJobs/blob/main/sql/v0.1.5/xact-jobs-sqlite-v0.1.5.sql), [v0.2.0](https://github.com/XactJobs/XactJobs/blob/main/sql/v0.2.0/xact-jobs-sqlite-v0.2.0.sql), [v0.3.0](https://github.com/XactJobs/XactJobs/blob/main/sql/v0.3.0/xact-jobs-sqlite-v0.3.0.sql) |

When upgrading an existing installation, run only the scripts of the newer versions. For v0.1.5, upgrade all the workers first and then run
the script, because it drops an index that older versions need.

For v0.2.0, run the script first: it adds the columns for batches and continuations, which v0.2.0 needs. Start using batches and continuations only
once all the workers run v0.2.0: a worker of an older version that retries a job of a batch removes the job from its batch, so the batch's continuations could start too early.

For v0.3.0, run the script first: it adds the column for the time a job was originally scheduled for, which v0.3.0 needs. Then upgrade the workers.
A worker of an older version that retries a job leaves the column empty, so that retry sees its own retry time as `XactJobContext.OriginallyScheduledAt`.
On SQLite, the script also adds an index on the queue and the scheduled time, for the claims: without it, every claim reads and sorts the whole job table.

## Using EF Migrations
Alternatively, instead of manually creating the required tables with the scripts above, you can include the XactJobs entities in EF migrations like this:
```csharp
public class UserDbContext: DbContext
{
    // Your DbSets...

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyXactJobsConfigurations(Database.ProviderName,
            excludeFromMigrations: false); // <--- key part here

        // Apply your entity configurations...
    }
}
```
And then create the migration like you normally would:
```bash
dotnet ef migrations add XactJobsTables
```

# Jobs

A job is simply a public method. It can be either a static or an instance method, but it must be public and:
- it must not be a generic method,
- it cannot have `out` or `ref` parameters,
- it cannot have parameters that are anonymous functions, delegates, or lambda expressions,
- if it is an async method, it cannot be `async void` (it must be `async Task`),

Job method parameters are evaluated immediately and serialized using `System.Text.Json.JsonSerializer`.
This means parameters can be both primitives or complex objects, although large objects should be avoided as they require more storage and take longer to serialize/deserialize.

## Job Examples

### Static methods

```csharp
class EmailHelper
{
    // example 1
    public static void SendEmail(string from, string to, string body)
    {
        // ... mail sending logic
    }

    // example 2
    public static async Task SendEmailAsync(string from, string to, string body, CancellationToken cancellationToken)
    {
        // ... mail sending logic
    }
}
```

These jobs can be scheduled with:

```csharp
// example 1
dbContext.JobEnqueue(
    () => EmailHelper.SendEmail("bob@acme.com", "alice@acme.com", "Hello Alice"));

// example 2
dbContext.JobEnqueue(
    () => EmailHelper.SendEmailAsync("bob@acme.com", "alice@acme.com", "Hello Alice", CancellationToken.None));

dbContext.SaveChanges();
```

If a method accepts a cancellation token, then `null` or `CancellationToken.None` should be passed when the job is enqueued.  
When the worker executes the job, it will pass the correct stopping token.

In the same way, a method can take an `XactJobContext` parameter, with `null` passed when the job is enqueued. The worker passes the context of the run.
For now it has `OriginallyScheduledAt`, the time (UTC) the job was originally scheduled for, which stays the same across its retries:

```csharp
public async Task SendDailyReportAsync(XactJobContext context, CancellationToken cancellationToken)
{
    var idempotencyKey = $"daily-report-{context.OriginallyScheduledAt:yyyy-MM-dd}";
    // ...
}

dbContext.JobEnsurePeriodic<ReportSender>(
    x => x.SendDailyReportAsync(null!, CancellationToken.None),
    "daily_report",
    Cron.Daily());
```

For a periodic job it's the cron slot of the run. For a continuation, it's the time its first attempt was claimed, once its batch was finished: the time is saved
when the first attempt is claimed, so a second run of that attempt, after its lease expired, gets the same value.

The cron slot and `OriginallyScheduledAt` are in UTC, because cron expressions are evaluated in UTC. So a daily run at midnight in a time zone east of UTC,
written as its UTC time (e.g. `0 0 22 * * *` for UTC+2), has the previous day's UTC date. If that's confusing, format the key with the full time, e.g. `:yyyyMMddHHmmss`.

### Instance methods

```csharp
class EmailSender
{
    private readonly IEmailService _emailService;

    // Constructor with dependencies.
    public EmailSender(IEmailService emailService)
    {
        _emailService = emailService;
    }

    // example 1
    public void SendEmail(string from, string to, string body)
    {
        // ... mail sending logic
    }

    // example 2
    public async Task SendEmailAsync(string from, string to, string body, CancellationToken cancellationToken)
    {
        // ... mail sending logic
    }
}
```

These jobs can be scheduled with:

```csharp
// example 1
dbContext.JobEnqueue<EmailSender>(
    x => x.SendEmail("bob@acme.com", "alice@acme.com", "Hello Alice"));

// example 2
dbContext.JobEnqueue<EmailSender>(
    x => x.SendEmailAsync("bob@acme.com", "alice@acme.com", "Hello Alice", CancellationToken.None));

dbContext.SaveChanges();
```

When a worker is processing an **instance** job method, it will use the first public constructor
to create the instance, and then call the method on that instance. If the constructor has parameters, the worker
will use `IServiceScopeFactory` to create a scope and then attempt to resolve each type using `scope.ServiceProvider.GetRequiredService`.

This means the dependencies (`IEmailService` in the example) of the owning class must be registered in the DI service collection.
The owning class itself (`EmailSender` in the example) does not have to be a registered service - it just has to have a public constructor.

# Configuration

```csharp
builder.Services.AddXactJobs<UserDbContext>(options =>
{
    options
        .WithWorkerCount(2)                         // workers polling the queue
        .WithMaxDegreeOfParallelism(8)              // jobs running at the same time, per worker (default: processor count)
        .WithPollingInterval(6)                     // seconds between polls, per worker
        .WithLeaseDuration(120)                     // seconds, extended automatically while a job is running
        .WithShutdownTimeout(10)                    // seconds running jobs get to finish on stop, before they are cancelled
        .WithPeriodicJobReconciliationInterval(300) // seconds between the checks for periodic jobs without a pending run
        .WithHistoryRetentionPeriodInDays(30)
        .WithSaveOutsideTransaction(XactJobSaveCheck.Warn); // when a job saves without a transaction: Warn, Throw or Ignore (all the queues)

    // isolated queues have their own settings
    options.WithLongRunningQueue(queue => queue.WithMaxDegreeOfParallelism(2));
});
```

The values above are the defaults, except for `WithMaxDegreeOfParallelism`.

# Dashboard

The `XactJobs.UI` package adds a web dashboard and a REST API to see and manage the jobs.

![The dashboard: due, running, succeeded and failed jobs, and the jobs processed per hour](https://raw.githubusercontent.com/XactJobs/XactJobs/main/docs/images/dashboard.png)

```csharp
builder.Services.AddXactJobsUI<UserDbContext>(options =>
{
    options
        .RequireAuthorizationPolicy("JobAdmins") // who can use it, see below
        .WithBasePath("/XactJobs")               // the default
        .WithApiBasePath("/api/XactJobs");       // the default
});

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapXactJobsUI();       // maps the pages (Razor Pages), the API, and the dashboard's scripts and styles
```

The dashboard's scripts, styles and fonts (Bootstrap, Bootstrap Icons, Chart.js and htmx) are embedded in the package, and `MapXactJobsUI` serves them
under the dashboard's path, with the same access as the pages. So the dashboard works without internet access, without the static files middleware,
and in any environment, whether the application is published or not.

## Access

By default the dashboard only accepts requests from the local machine. Choose who can use it:

- `RequireAuthorization()` - any signed-in user (the default authorization policy)
- `RequireAuthorizationPolicy("name")` - users that satisfy the policy
- `AllowAnonymous()` - anyone. Only for development, or when the access is restricted in another way
- `AllowLocalRequestsOnly()` - the default. Behind a reverse proxy on the same machine, every request looks local, unless the forwarded headers middleware is configured

The API endpoints that change data require an antiforgery token, which the dashboard sends. If the API is called by clients that don't authenticate with cookies, `DisableAntiforgery()` turns the check off.

## Pages and Actions

- **Dashboard** - due, scheduled and running jobs, continuations waiting for their batch, succeeded and failed jobs in the last 24 hours, and a chart per hour
- **Scheduled** - the pending and running jobs. A continuation that waits for its batch shows how many of the batch's jobs are left, and jobs of batches link to their batch
  - *Run now* makes a pending job due now, and the job keeps the time it was scheduled for as `XactJobContext.OriginallyScheduledAt`. A continuation that waits for its batch can't: it starts when the batch is finished
  - *Cancel* moves a pending job to the history as cancelled. Running jobs can't be cancelled, and neither can runs of periodic jobs: pause the periodic job instead.
    A cancelled job of a batch counts as a failed job of the batch, and if it was the last one, the batch's continuations start
- **History** - succeeded, failed (out of retries, or cancelled by the job with `XactJobCancelException`), cancelled from the dashboard, retried attempts, and skipped runs and continuations, with the reason
  - *Retry* enqueues a failed or cancelled job, or a skipped continuation, again as a new job. A run of a periodic job is enqueued as a one-off job,
    and a job of a batch or a continuation runs outside of any batch, so it doesn't start continuations. Like a retry by a worker, it keeps the time the job was originally scheduled for
- **Batches** - the active batches, with jobs left, and the finished ones, as long as their history is kept, with their progress
  - A batch's page has its jobs left, its continuations and its history, and links to the batch it continues
- **Periodic** - the periodic jobs, with their next run
  - *Pause* removes the pending run, and no runs are scheduled until it's resumed. A paused job stays paused when the application starts again, even if `JobEnsurePeriodic` or `WithPeriodicJob` defines it
  - *Resume* schedules the next run
  - *Trigger* makes the pending run due now, so the job keeps a single schedule
  - *Delete* deletes the definition. A periodic job defined in code is added again when the application starts

![Scheduled jobs: running, scheduled and retrying jobs, with their queues](https://raw.githubusercontent.com/XactJobs/XactJobs/main/docs/images/scheduled.png)

![A failed job's error message and stack trace](https://raw.githubusercontent.com/XactJobs/XactJobs/main/docs/images/failed-job.png)

![Periodic jobs: their cron expressions and next runs, one of them paused](https://raw.githubusercontent.com/XactJobs/XactJobs/main/docs/images/periodic.png)

![Active batches: their progress, failed jobs and waiting continuations](https://raw.githubusercontent.com/XactJobs/XactJobs/main/docs/images/batches.png)

![A batch: its jobs left, the continuations that wait for it, and its history](https://raw.githubusercontent.com/XactJobs/XactJobs/main/docs/images/batch.png)

A job that is running can't be changed: the dashboard never touches a job a worker has leased.

Pausing and resuming are also available in code, with `dbContext.JobSetPeriodicActiveAsync(id, isActive, cancellationToken)` followed by `SaveChanges`.

## API

All under the API base path (`/api/XactJobs` by default):

| Method | Path | |
|---|---|---|
| GET | `/stats` | Dashboard counts |
| GET | `/chart-data?from=&to=&intervalMinutes=` | Counts per interval (default: the last 24 hours) |
| GET | `/jobs?page=&pageSize=&queue=` | Pending and running jobs |
| GET | `/jobs/{id}` | A pending or running job |
| POST | `/jobs/{id}/run-now` | Make a pending job due now |
| POST | `/jobs/{id}/cancel` | Cancel a pending job |
| GET | `/history?category=&page=&pageSize=&queue=` | History: `succeeded`, `failed`, `cancelled`, `retried` or `skipped` |
| GET | `/history/{id}` | A history row |
| POST | `/history/{id}/retry` | Enqueue a failed or cancelled job, or a skipped continuation, again |
| GET | `/batches?state=&page=&pageSize=` | Batches: `active` or `finished` |
| GET | `/batches/{id}` | A batch with its jobs, history and continuations |
| GET | `/periodic?page=&pageSize=` | Periodic jobs |
| GET | `/periodic/{id}` | A periodic job |
| POST | `/periodic/{id}/pause` | Pause a periodic job |
| POST | `/periodic/{id}/resume` | Resume a periodic job |
| POST | `/periodic/{id}/trigger` | Run a periodic job now |
| DELETE | `/periodic/{id}` | Delete a periodic job |

The actions answer `200` with a message, `404` if the job doesn't exist, or `409` if its state doesn't allow the action (e.g. it's running).

# How Jobs Run

## Polling
A worker only claims jobs it can start immediately, up to `MaxDegreeOfParallelism` at a time, so claimed jobs never wait behind a slow job while another
worker is free. A slow job only occupies its own slot, and the worker keeps claiming jobs for the other slots.

If a poll fills all the free slots there is likely a backlog, so the worker polls again as soon as a slot frees up. Otherwise it waits for
the polling interval or a QuickPoll notification. Polling can stop only if all the slots are occupied, so long-running jobs are best kept in their own queue.

## Failures and Retries

When a job throws, its pending database changes are discarded, the attempt is recorded in the history as failed, and the job is scheduled to run again by the retry strategy. By default, it runs up to 10 times, waiting longer after each attempt (2 seconds at first, up to an hour), and a job that runs out of retries is recorded as cancelled. `WithRetryStrategy` sets another strategy (an `IRetryStrategy`).

A job that finds it can never succeed, e.g. because the data it works on is gone, throws `XactJobCancelException` to stop without retries:

```csharp
public async Task SendInvoiceAsync(int orderId)
{
    var order = await db.Orders.FindAsync(orderId)
        ?? throw new XactJobCancelException($"Order {orderId} doesn't exist anymore");

    // ...
}
```

The job moves to the history as cancelled right away, with the exception's message as the error. As with any failure, its pending database changes are discarded. The exception is also found when it's wrapped in other exceptions. For a periodic job, only the current run is cancelled: the next one is scheduled as usual.

A job that has nothing to do should just return: it's recorded as completed.

A custom retry strategy gets the job that failed. For the first attempt of a continuation, its `ScheduledAt` is the reserved time 9999-12-31, so base the retry time on the current time.

## Stopping
When the application stops, the workers stop polling and running jobs get `ShutdownTimeoutInSeconds` to finish. Then the cancellation token passed to the jobs is cancelled, and they get
5 more seconds to react. Finally, the leases of the unfinished jobs are cleared so they can run again right away, which takes up to `ClearLeaseTimeoutInSeconds`.
A cancelled job doesn't count as a failed attempt.

With the default settings this fits in the default 30 second shutdown timeout of the .NET host. If you lower that timeout, lower these as well.
`WithShutdownTimeout(0)` cancels the running jobs immediately.

A job that ignores the cancellation token may still be running when its lease is cleared, so it can end up running on another worker at the same time, which the next section covers.

## Exactly-Once Database Effects
A lease can expire while a job is still running, for example if a node loses its database connection for a while. Another worker can then run the same job. For the database writes
this is safe, as long as the job uses the scoped `TDbContext` injected into its class and does one of these two things:

- **Doesn't save its changes.** The worker saves them when the job finishes, as in the [example above](#a-job-is-a-unit-of-work).
- **Saves its changes inside a transaction it doesn't commit.** The worker commits it when the job finishes:
  ```csharp
  public async Task CreateInvoiceAsync(int orderId, CancellationToken cancellationToken)
  {
      await db.Database.BeginTransactionAsync(cancellationToken);

      db.Invoices.Add(new Invoice(orderId));
      await db.SaveChangesAsync(cancellationToken);

      // ... more work that needs the saved invoice
  }
  ```

Either way the job's writes, the removal of the job row and the new history row all commit in one transaction. If two workers run the same job, only one of them can commit:
the other one's delete affects 0 rows, EF throws a concurrency exception, and its writes roll back. The same happens if the job fails.

**This doesn't apply to** changes the job saves without a transaction, or commits itself, because they are already permanent when the job finishes.
It also doesn't apply to other DbContext instances, or to external side effects like sending emails or calling HTTP APIs. These can happen more than once, so they should be idempotent.

**The worker warns when a job saves without a transaction.** While a job runs, the worker checks the saves on the scoped `TDbContext` it created for the job.
If the job calls `SaveChanges` without a transaction, the worker logs a warning that names the job, once per job method, and the save goes ahead.
`WithSaveOutsideTransaction` changes this: with `XactJobSaveCheck.Throw`, `SaveChanges` throws instead, so nothing is saved, and the job fails and is retried as after any other exception.
`XactJobSaveCheck.Ignore` turns the check off. Only `SaveChanges` on that `TDbContext` is checked: not other DbContext instances, not `ExecuteUpdate`, `ExecuteDelete`
or raw SQL, which also run right away without a transaction, and not a transaction the job commits itself.

## Idempotent External Calls
Calls to other systems, like a payment provider or an email API, are at least once: a job that fails after the call, or whose lease expired, runs the call again.
To make them safe, send an idempotency key with the call, and pass it to the job as a method argument:

- a business key, like `$"charge-order-{orderId}"`,
- or `Guid.NewGuid()`: method arguments are evaluated once, when the job is enqueued, so every retry gets the same key.

```csharp
dbContext.JobEnqueue<PaymentJob>(x => x.ChargeAsync(orderId, Guid.NewGuid(), CancellationToken.None));
```

A periodic job gets the same arguments on every run, so build its key from the run's cron slot instead, with
[`XactJobContext.OriginallyScheduledAt`](#static-methods), e.g. `$"daily-report-{context.OriginallyScheduledAt:yyyy-MM-dd}"`.

The receiving system has to support idempotency keys: it does the work for the first call with a key, and answers the same for the next ones.

# Database Notes

## PostgreSQL
The job table is a high-churn queue: every job inserts, updates and deletes a row. Aggressive autovacuum settings for this table are recommended, for example:

```sql
ALTER TABLE xact_jobs.job SET (
  autovacuum_vacuum_scale_factor = 0,
  autovacuum_vacuum_threshold = 1000,
  autovacuum_vacuum_cost_limit = 2000
);
```

Long-running transactions **anywhere in the cluster** hold back the cleanup of dead rows, not just the ones using this table. Dead rows pile up meanwhile, which slows down fetching jobs.

## MySQL
XactJobs needs **MySQL 8.0 or later**, because claiming jobs uses `SKIP LOCKED` and `FOR UPDATE OF`. MariaDB isn't supported: it doesn't support `FOR UPDATE OF`, and only supports `SKIP LOCKED` from 10.6.

The workers claim jobs at `READ COMMITTED`, not at InnoDB's default `REPEATABLE READ`. At `REPEATABLE READ`, a claim would keep the running jobs it scans locked, and the gaps between rows too, so finished jobs couldn't be deleted, and new jobs (`JobEnqueue` and `SaveChanges`) couldn't be saved until the claim commits. Only the claims use it: your own transactions, and the jobs, keep their isolation level.

The provider depends on the version of .NET:

| .NET | MySQL provider |
|---|---|
| .NET 8 | `Pomelo.EntityFrameworkCore.MySql` |
| .NET 9 | `Pomelo.EntityFrameworkCore.MySql` or `MySql.EntityFrameworkCore` |
| .NET 10 | `MySql.EntityFrameworkCore` |

Pomelo doesn't support EF Core 10 yet, and on .NET 8 the official provider only supports EF Core 8. These combinations build, but fail at runtime.

**With the official provider (`MySql.EntityFrameworkCore`), `SaveChanges` doesn't start a transaction on its own.** If a statement fails, the ones before it stay committed. This
can break the main promise of XactJobs in your own code: for example, a job can be saved without the business data it belongs to.

To make your saves all-or-nothing, either use a transaction, as in [Transactions](#transactions), or set this on your `DbContext`:

```csharp
public class UserDbContext : DbContext
{
    public UserDbContext(DbContextOptions<UserDbContext> options) : base(options)
    {
        Database.AutoTransactionBehavior = AutoTransactionBehavior.Always;
    }
}
```

The workers already do this for their own saves, so running jobs are not affected. Pomelo starts a transaction when a save has more than one statement.

## Oracle
XactJobs needs **Oracle 12c or later**. A claim is one PL/SQL block, which returns the claimed jobs with `DBMS_SQL.RETURN_RESULT`.

Jobs are claimed oldest first. The block locks the due jobs with `FOR UPDATE SKIP LOCKED`, fetches only as many as the worker has free slots, and leases them. Oracle locks a row only when it's fetched, so workers that poll at once each get different jobs from a backlog. `ROWNUM` can't be used to limit the rows, because it's applied before locked rows are skipped: all the workers would try the same first rows, and all but one would get none.

The claims read the table's primary key in order (with an index hint), so a claim doesn't sort the whole backlog, even if Oracle cached a plan from when the table was nearly empty.

# License

**XactJobs** is licensed under the  
**GNU Lesser General Public License version 3.0 or later (LGPL-3.0-or-later).**

## Included Third-Party Code

- The file [Internal/XactJobSerializer.cs](https://github.com/XactJobs/XactJobs/blob/main/src/XactJobs/Internal/XactJobSerializer.cs) includes code adapted from **Hangfire**,  
  licensed under the **LGPL-3.0-or-later**.  
  Copyright © 2013-2014 Hangfire OÜ.  
  See [http://www.gnu.org/licenses/](http://www.gnu.org/licenses/).

- Source files in the folder [Internal/ExpressionUtil](https://github.com/XactJobs/XactJobs/tree/main/src/XactJobs/Internal/ExpressionUtil) are copied unchanged from the **ASP.NET MVC** project,  
  licensed under the **Apache License 2.0**.  
  Copyright © .NET Foundation.  
  See [http://www.apache.org/licenses/LICENSE-2.0](http://www.apache.org/licenses/LICENSE-2.0).

- The dashboard (`XactJobs.UI`) includes [Bootstrap](https://getbootstrap.com) 5.3.3, [Bootstrap Icons](https://icons.getbootstrap.com) 1.11.3,
  [Chart.js](https://www.chartjs.org) 4.4.1 and [htmx](https://htmx.org) 1.9.10, each licensed under the **MIT License**.
  Their license texts are next to them, in [Assets/lib](https://github.com/XactJobs/XactJobs/tree/main/src/XactJobs.UI/Assets/lib).

## License Texts

- The full text of the LGPL-3.0 license is available in the `COPYING.LESSER` file.  
- The Apache License 2.0 text is available in the `LICENSE-ASP.NET.txt` file.

## Disclaimer

XactJobs is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the respective license files for details.
