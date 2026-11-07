---
title: Failures, retries and stopping
description: What happens when a job throws, the default retry strategy, XactJobCancelException, and how workers stop.
---

## Failures and retries

When a job throws, its pending database changes are discarded, the attempt is recorded in the history as failed, and the job is scheduled
to run again by the retry strategy. A job that runs out of retries is recorded as cancelled.

By default (`DefaultRetryStrategy`) a job runs up to 10 times, waiting longer after each failed attempt:

| After failed attempt | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 |
|---|---|---|---|---|---|---|---|---|---|---|
| The next attempt runs in | 2 s | 2 s | 5 s | 10 s | 30 s | 1 min | 5 min | 15 min | 30 min | none: cancelled |

`WithRetryStrategy` sets another strategy, per queue. `DefaultRetryStrategy` takes the number of attempts and the waits in seconds, and
uses the last wait for the attempts after them:

```csharp
builder.Services.AddXactJobs<UserDbContext>(options =>
{
    options.WithRetryStrategy(new DefaultRetryStrategy(maxAttempts: 5, retrySeconds: [10, 60, 300]));
});
```

Or implement `IRetryStrategy`, whose `GetRetryTimeUtc(job, newErrorCount)` returns the time of the next attempt in UTC, or `null` for no
more attempts. A custom retry strategy gets the job that failed. For the first attempt of a continuation, its `ScheduledAt` is the reserved
time 9999-12-31, so base the retry time on the current time.

## Stopping a job without retries

A job that finds it can never succeed, e.g. because the data it works on is gone, throws `XactJobCancelException` to stop without retries:

```csharp
public async Task SendInvoiceAsync(int orderId)
{
    var order = await db.Orders.FindAsync(orderId)
        ?? throw new XactJobCancelException($"Order {orderId} doesn't exist anymore");

    // ...
}
```

The job moves to the history as cancelled right away, with the exception's message as the error. As with any failure, its pending
database changes are discarded. The exception is also found when it's wrapped in other exceptions. For a periodic job, only the current
run is cancelled: the next one is scheduled as usual.

A job that has nothing to do should just return: it's recorded as completed.

## Stopping

When the application stops, the workers stop polling and running jobs get `ShutdownTimeoutInSeconds` (default 10) to finish. Then the
cancellation token passed to the jobs is cancelled, and they get 5 more seconds to react. Finally, the leases of the unfinished jobs are
cleared so they can run again right away, which takes up to `ClearLeaseTimeoutInSeconds` (default 10). A cancelled job doesn't count as a
failed attempt.

With the default settings this fits in the default 30 second shutdown timeout of the .NET host. If you lower that timeout, lower these as
well. `WithShutdownTimeout(0)` cancels the running jobs immediately.

A job that ignores the cancellation token may still be running when its lease is cleared, so it can end up running on another worker at
the same time. Its database changes are still saved [only once](../unit-of-work/#exactly-once-database-effects).
