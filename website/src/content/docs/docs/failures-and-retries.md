---
title: Failures, retries and stopping
description: What happens when a job throws, the retry strategies, retries per job method, XactJobCancelException, and how workers stop.
---

## Failures and retries

When a job throws, its pending database changes are discarded, the attempt is recorded in the history as failed, and the job is scheduled
to run again by the retry strategy. A job that runs out of retries is recorded as cancelled. The failure is recorded on a connection of its
own, tried up to 3 times, a second apart, if the database closed it (a restart, a failover); if it still can't be saved, the job runs again
when its lease expires, without counting as a failed attempt. A retry is the same job: it keeps its id, so its attempts have the same id in the history, the logs and the traces.

By default (`DefaultRetryStrategy`) a failed job is retried up to 10 times, waiting longer before each retry:

| Retry | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 |
|---|---|---|---|---|---|---|---|---|---|---|
| Wait before it | 2 s | 2 s | 5 s | 10 s | 30 s | 1 min | 5 min | 15 min | 30 min | 1 h |

A job whose 10th retry fails is recorded as cancelled.

`WithRetryStrategy` sets another strategy, per queue. `DefaultRetryStrategy` takes the number of retries and the wait before each one in
seconds, and uses the last wait for the retries beyond the list:

```csharp
builder.Services.AddXactJobs<UserDbContext>(options =>
{
    options.WithRetryStrategy(new DefaultRetryStrategy(maxRetries: 3, retrySeconds: [10, 60, 300]));
});
```

Or implement `IRetryStrategy`, whose `GetRetryTimeUtc(job, newErrorCount, exception)` returns the time of the next attempt in UTC, or
`null` for no more attempts. It gets the job that failed and what the attempt threw, e.g. to retry a timeout sooner than a rate limit:

```csharp
public class HttpRetryStrategy : IRetryStrategy
{
    private readonly DefaultRetryStrategy _default = new();

    public DateTime? GetRetryTimeUtc(XactJob job, int newErrorCount, Exception exception)
    {
        if (newErrorCount > 7) return null; // up to 7 retries

        return exception is HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests }
            ? DateTime.UtcNow.AddMinutes(1)
            : _default.GetRetryTimeUtc(job, newErrorCount, exception);
    }
}
```

For the first attempt of a continuation, the job's `ScheduledAt` is the reserved time 9999-12-31, so base the retry time on the current
time.

### Retries of a job method

`[XactRetry]` on a job method replaces its queue's strategy for that method: the number of retries, and the wait before each one in
seconds, as `DefaultRetryStrategy` takes them. Without waits, it waits as `DefaultRetryStrategy` does by default.

```csharp
public class InvoiceJobs
{
    // no retries: a second email is worse than none
    [XactRetry(0)]
    public async Task SendReminderAsync(int invoiceId)
    {
        // ...
    }

    // up to 3 retries: after 10 seconds, a minute, then 5 minutes
    [XactRetry(3, [10, 60, 300])]
    public async Task ExportAsync(int invoiceId)
    {
        // ...
    }
}
```

It's read when an attempt fails, so changing it applies to the jobs already enqueued too. An invalid one (negative retries or waits)
throws when the job is enqueued. An override of a job method inherits it.

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
