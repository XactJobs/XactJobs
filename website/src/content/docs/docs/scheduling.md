---
title: Scheduling
description: Fire-and-forget and delayed jobs, and several jobs in one transaction.
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

Periodic (recurring) jobs run on a cron schedule: see [Periodic jobs](../periodic-jobs/).
