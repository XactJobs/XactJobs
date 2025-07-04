# XactJobs
XactJobs lets you schedule and run background jobs **with full database transaction support** using EntityFrameworkCore. 
Jobs are saved alongside your business data and only execute if your transaction commits - no more “ghost” jobs on rollback.

Supports SQL Server, PostgreSQL, MySQL, Oracle, and SQLite. 

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
Recurring jobs run periodically according to the specified cron schedule.

```csharp
dbContext.JobEnsurePeriodic(
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

Finally create the XactJobs tables in your database using the SQL scripts below:
- [SQL Server](https://github.com/XactJobs/XactJobs/blob/main/sql/v0.1.0/xact-jobs-sqlserver-v0.1.0.sql)
- [PostgreSQL](https://github.com/XactJobs/XactJobs/blob/main/sql/v0.1.0/xact-jobs-postgresql-v0.1.0.sql)
- [MySQL](https://github.com/XactJobs/XactJobs/blob/main/sql/v0.1.0/xact-jobs-mysql-v0.1.0.sql)
- [Oracle](https://github.com/XactJobs/XactJobs/blob/main/sql/v0.1.0/xact-jobs-oracle-v0.1.0.sql)
- [SQLite](https://github.com/XactJobs/XactJobs/blob/main/sql/v0.1.0/xact-jobs-sqlite-v0.1.0.sql)

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
