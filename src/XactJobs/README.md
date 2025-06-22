# XactJobs
XactJobs lets you schedule and run background jobs **with full database transaction support** using EntityFrameworkCore. 
Jobs are saved alongside your business data and only execute if your transaction commits - no more “ghost” jobs on rollback.

Supports SqlServer, PostgreSql, MySql, and Oracle. 

## Development Status
This library is still in early development, but the users are invited to test and report issues.

## Features
### Fire and Forget Jobs
Fire-and-forget jobs are executed once by a background worker in the next polling interval (configurable, default 3s).

```csharp
// ... other business logic
dbContext.JobEnqueue(
    () => Console.WriteLine("Fire-and-forget!"));

dbContext.SaveChanges();
```

### Delayed Jobs
Delayed jobs are executed once after the specified time interval.

```csharp
dbContext.JobScheduleIn(
    () => Console.WriteLine("Delayed!"),
    TimeSpan.FromDays(7));

dbContext.SaveChanges();
```

### Recurring jobs
Recurring jobs run periodically according to a specified cron schedule.

```csharp
dbContext.JobEnsureRecurring(
    () => Console.WriteLine("Recurring!"),
    "my_recurring_job",
    Cron.Daily());

dbContext.SaveChanges();
```

### Batches
Adding multiple jobs before saving changes will enqueue all jobs in a single transaction.

```csharp
dbContext.JobEnqueue(() => Console.WriteLine("Job 1"));
dbContext.JobEnqueue(() => Console.WriteLine("Job 2"));

dbContext.SaveChanges();
```

### Transactions
Jobs participate in DbContext transactions, just like the regular entities in EFCore.

```csharp
using var tx = dbContext.Database.BeginTransaction();

// some business logic
dbContext.JobEnqueue(() => Console.WriteLine("Job 1"));
dbContext.SaveChanges();

// some more business logic
dbContext.JobEnqueue(() => Console.WriteLine("Job 2"));
dbContext.SaveChanges();

tx.Commit();
```
### Isolated Queues
XactJobs can be configured to run isolated workers for named queues.
```csharp
dbContext.JobEnqueue(() => Console.WriteLine("Job 1"), QueueNames.Priority);
dbContext.JobEnqueue(() => Console.WriteLine("Job 2"), QueueNames.LongRunning);

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
        _quickPoll.JobEnqueue(() => Console.WriteLine("QuickPoll"));

        // Save changes through _quickPoll
        // (this calls DbContext.SaveChanges and notifies the workers)
        _quickPoll.SaveChangesAndNotify(); 
    }
}
```
This can only work if the workers are running in the same process where the jobs are enqueued (which is the default).

# Installation
TODO

# Configuration
TODO