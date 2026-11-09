---
title: Jobs
description: What a job method can be, static and instance methods, dependency injection, parameters, CancellationToken and XactJobContext.
---

A job is simply a public method. It can be either a static or an instance method, but it must be public and:

- it must not be a generic method,
- it cannot have `out` or `ref` parameters,
- it cannot have parameters that are anonymous functions, delegates, or lambda expressions,
- if it is an async method, it cannot be `async void` (it must be `async Task`).

## Parameters

Job method parameters are evaluated immediately, when the job is enqueued, and serialized using `System.Text.Json.JsonSerializer`.
This means parameters can be both primitives or complex objects, although large objects should be avoided, as they require more storage
and take longer to serialize and deserialize.

Because the arguments are evaluated when the job is enqueued, a key the database generates on `SaveChanges` isn't known yet: pass a
value you already have, like a business key or a `Guid` you create.

The method is stored with its parameter types, so overloaded methods can be jobs.

## Static methods

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

## Instance methods and dependency injection

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

When a worker is processing an **instance** job method, it uses the first public constructor to create the instance, and then calls the
method on that instance. If the constructor has parameters, the worker uses `IServiceScopeFactory` to create a scope and then resolves
each type with `scope.ServiceProvider.GetRequiredService`.

This means the dependencies (`IEmailService` in the example) of the owning class must be registered in the DI service collection.
The owning class itself (`EmailSender` in the example) does not have to be a registered service: it just has to have a public constructor.

A job that takes your `TDbContext` in its constructor gets the scoped instance the worker saves when the job finishes. That's what makes
a job a [unit of work](../unit-of-work/).

## CancellationToken

If a method accepts a cancellation token, then `null` or `CancellationToken.None` should be passed when the job is enqueued.
When the worker executes the job, it passes the correct stopping token. See [Stopping](../failures-and-retries/#stopping) for when it's cancelled.

## XactJobContext

In the same way, a method can take an `XactJobContext` parameter, with `null` passed when the job is enqueued. The worker passes the
context of the run. For now it has `OriginallyScheduledAt`, the time (UTC) the job was originally scheduled for, which stays the same
across its retries:

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

- For a periodic job it's the cron slot of the run.
- For a continuation, it's the time its first attempt was picked up, once its batch was finished. That time is saved, so a second run of
  that attempt, after its lease expired, gets the same value.
- The dashboard's *Run now*, *Trigger* and *Retry* keep it too.

The cron slot and `OriginallyScheduledAt` are in UTC, because cron expressions are evaluated in UTC. So a daily run at midnight in a time
zone east of UTC, written as its UTC time (e.g. `0 0 22 * * *` for UTC+2), has the previous day's UTC date. If that's confusing, format
the key with the full time, e.g. `:yyyyMMddHHmmss`.

[Idempotent external calls](../idempotent-external-calls/) shows how to use it.
