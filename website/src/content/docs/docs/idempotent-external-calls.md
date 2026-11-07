---
title: Idempotent external calls
description: Calls to other systems are at least once. Pass an idempotency key as a job argument, or build it from OriginallyScheduledAt for periodic jobs.
---

Calls to other systems, like a payment provider or an email API, are at least once: a job that fails after the call, or whose lease
expired, runs the call again. To make them safe, send an idempotency key with the call, and pass it to the job as a method argument:

- a business key, like `$"charge-order-{orderId}"`,
- or `Guid.NewGuid()`: method arguments are evaluated once, when the job is enqueued, so every retry gets the same key.

```csharp
dbContext.JobEnqueue<PaymentJob>(x => x.ChargeAsync(orderId, Guid.NewGuid(), CancellationToken.None));
```

## Periodic jobs

A periodic job gets the same arguments on every run, so build its key from the run's cron slot instead, with
[`XactJobContext.OriginallyScheduledAt`](../jobs/#xactjobcontext), e.g. `$"daily-report-{context.OriginallyScheduledAt:yyyy-MM-dd}"`:

```csharp
public async Task SendDailyReportAsync(XactJobContext context, CancellationToken cancellationToken)
{
    var idempotencyKey = $"daily-report-{context.OriginallyScheduledAt:yyyy-MM-dd}";
    // ...
}
```

`OriginallyScheduledAt` doesn't change when a job is retried, and it's in UTC: see [XactJobContext](../jobs/#xactjobcontext).

## The receiving side

The receiving system has to support idempotency keys: it does the work for the first call with a key, and answers the same for the next ones.

The job's own database changes don't need a key: the worker saves them [exactly once](../unit-of-work/#exactly-once-database-effects).
