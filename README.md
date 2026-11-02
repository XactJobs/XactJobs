# XactJobs
XactJobs lets you schedule and run background jobs **with full database transaction support** using EntityFrameworkCore. 
Jobs are saved alongside your business data and only execute if your transaction commits - no more “ghost” jobs on rollback.

Supports **SQL Server, PostgreSQL, MySQL, Oracle and SQLite**, on .NET 8, 9 and 10.

XactJobs uses EF Core 9 on .NET 8 and 9, and EF Core 10 on .NET 10, so your database provider has to support that version. For MySQL, see [which provider to use](#mysql).

## Development Status
This library is still in early development, but the users are invited to test and report issues.

## Features
The examples below use this simple job:

```csharp
public static class Greeter
{
    public static void Say(string message) => Console.WriteLine(message);
}
```

Calling `Console.WriteLine` directly wouldn't work, because a job method can't have overloads with the same number of parameters (see [Jobs](#jobs)).

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

### Batches
Adding multiple jobs before saving changes will enqueue all jobs in a single transaction.

```csharp
dbContext.JobEnqueue(() => Greeter.Say("Job 1"));
dbContext.JobEnqueue(() => Greeter.Say("Job 2"));

dbContext.SaveChanges();
```

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
| SQL Server | [v0.1.0](https://github.com/XactJobs/XactJobs/blob/main/sql/v0.1.0/xact-jobs-sqlserver-v0.1.0.sql), [v0.1.4](https://github.com/XactJobs/XactJobs/blob/main/sql/v0.1.4/xact-jobs-sqlserver-v0.1.4.sql), [v0.1.5](https://github.com/XactJobs/XactJobs/blob/main/sql/v0.1.5/xact-jobs-sqlserver-v0.1.5.sql) |
| PostgreSQL | [v0.1.0](https://github.com/XactJobs/XactJobs/blob/main/sql/v0.1.0/xact-jobs-postgresql-v0.1.0.sql), [v0.1.4](https://github.com/XactJobs/XactJobs/blob/main/sql/v0.1.4/xact-jobs-postgresql-v0.1.4.sql), [v0.1.5](https://github.com/XactJobs/XactJobs/blob/main/sql/v0.1.5/xact-jobs-postgresql-v0.1.5.sql) |
| MySQL | [v0.1.0](https://github.com/XactJobs/XactJobs/blob/main/sql/v0.1.0/xact-jobs-mysql-v0.1.0.sql), [v0.1.4](https://github.com/XactJobs/XactJobs/blob/main/sql/v0.1.4/xact-jobs-mysql-v0.1.4.sql), [v0.1.5](https://github.com/XactJobs/XactJobs/blob/main/sql/v0.1.5/xact-jobs-mysql-v0.1.5.sql) |
| Oracle | [v0.1.0](https://github.com/XactJobs/XactJobs/blob/main/sql/v0.1.0/xact-jobs-oracle-v0.1.0.sql), [v0.1.4](https://github.com/XactJobs/XactJobs/blob/main/sql/v0.1.4/xact-jobs-oracle-v0.1.4.sql), [v0.1.5](https://github.com/XactJobs/XactJobs/blob/main/sql/v0.1.5/xact-jobs-oracle-v0.1.5.sql) |
| SQLite | [v0.1.0](https://github.com/XactJobs/XactJobs/blob/main/sql/v0.1.0/xact-jobs-sqlite-v0.1.0.sql), [v0.1.4](https://github.com/XactJobs/XactJobs/blob/main/sql/v0.1.4/xact-jobs-sqlite-v0.1.4.sql), [v0.1.5](https://github.com/XactJobs/XactJobs/blob/main/sql/v0.1.5/xact-jobs-sqlite-v0.1.5.sql) |

When upgrading an existing installation, run only the scripts of the newer versions. For v0.1.5, upgrade all the workers first and then run
the script, because it drops an index that older versions need.

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
- it cannot have an overload with **the same name and the same number of parameters** (this restriction may be removed in the future).

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
        .WithHistoryRetentionPeriodInDays(30);

    // isolated queues have their own settings
    options.WithLongRunningQueue(queue => queue.WithMaxDegreeOfParallelism(2));
});
```

The values above are the defaults, except for `WithMaxDegreeOfParallelism`.

# How Jobs Run

## Polling
A worker only claims jobs it can start immediately, up to `MaxDegreeOfParallelism` at a time, so claimed jobs never wait behind a slow job while another
worker is free. A slow job only occupies its own slot, and the worker keeps claiming jobs for the other slots.

If a poll fills all the free slots there is likely a backlog, so the worker polls again as soon as a slot frees up. Otherwise it waits for
the polling interval or a QuickPoll notification. Polling can stop only if all the slots are occupied, so long-running jobs are best kept in their own queue.

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

- **Doesn't save its changes.** The worker saves them when the job finishes:
  ```csharp
  public class InvoiceJob(UserDbContext db)
  {
      public void CreateInvoice(int orderId)
      {
          db.Invoices.Add(new Invoice(orderId));
      }
  }
  ```
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
Jobs are not claimed strictly oldest first, because Oracle doesn't allow `FOR UPDATE` together with an ordered subquery or `FETCH FIRST`.

Oracle also reuses cached query plans. If the first claim runs while the table is nearly empty, it can get a full table scan, and may keep that plan for a while as the table grows, until the plan is refreshed. Gathering statistics on the table is the usual way to refresh it.

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

## License Texts

- The full text of the LGPL-3.0 license is available in the `COPYING.LESSER` file.  
- The Apache License 2.0 text is available in the `LICENSE-ASP.NET.txt` file.

## Disclaimer

XactJobs is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the respective license files for details.
