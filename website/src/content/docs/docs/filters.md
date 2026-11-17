---
title: Job filters and failure handlers
description: Code around every job's run, in the job's scope and unit of work, e.g. to set up a tenant; and handlers told when a job has failed for good.
---

## Job filters

A job filter runs code around every job: set up a tenant from the job's arguments, open a log scope, time it. It implements
`IXactJobFilter`, and calls `next` to run the job:

```csharp
public class TenantFilter(ITenantContext tenant) : IXactJobFilter
{
    public async Task InvokeAsync(XactJobExecutingContext context, Func<Task> next)
    {
        // the job method's tenantId parameter, if it has one
        var index = Array.FindIndex(context.Method.GetParameters(), p => p.Name == "tenantId");

        if (index >= 0)
        {
            tenant.Set((int)context.Arguments[index]!);
        }

        await next();
    }
}
```

```csharp
builder.Services.AddXactJobs<AppDbContext>(options =>
{
    options
        .WithJobFilter<LoggingFilter>()  // outermost
        .WithJobFilter<TenantFilter>();  // then this one, then the job
});
```

A filter is resolved from the job's own scope, so it gets the same scoped services as the job, and the same DbContext: a tenant set on a
scoped service is the one the job's DbContext and its query filters see. `WithJobFilter` registers it as a scoped service, unless it's
registered already. Filters apply to all the queues, the first one added outermost.

The context has the job (`Job`: its id, queue, type and method names, scheduled time), the method (`Method`) and the arguments the
method gets (`Arguments`, in the order of its parameters), the attempt (`Attempt`: 1 for the first), the job's scope (`Services`), its
DbContext (`DbContext`), its [`XactJobContext`](../jobs/#xactjobcontext) (`JobContext`) and the cancellation token the job gets (`CancellationToken`).

A filter runs inside the job's [unit of work](../unit-of-work/):

- Changes it makes through the scoped DbContext are saved with the job's, in the transaction that completes it, and its own
  `SaveChanges` is checked like the job's (see [the save check](../unit-of-work/#the-save-check)): a warning names the job's method.
- An exception it throws is the job's failure, and is retried like one.
- It can catch the job's exception, e.g. to throw `XactJobCancelException` instead, so that the job isn't retried. It can't swallow it: if
  the job threw, the attempt fails with the job's exception also when the filter returns, because the job's changes, maybe half done,
  can't be saved as a completed job's. A job that should complete despite an error catches it itself.
- If it doesn't call `next`, the job completes without running. Calling `next` twice throws: a job runs once per attempt.

Filters run only when the job runs: not for a skipped run, like a paused periodic job or a continuation whose condition isn't met. The
arguments are read before them, so a job whose arguments can't be read fails before its filters run.

## Failure handlers

A failure handler is told when a job has failed for good: it's out of retries, or it threw `XactJobCancelException`. Not for the attempts
that are retried: each job is reported once. E.g. to send an alert:

```csharp
public class AlertOnFailure(IAlerts alerts) : IXactJobFailureHandler
{
    public async Task OnFailedAsync(XactJobFailedContext context, CancellationToken cancellationToken)
    {
        if (context.IsCancelledByJob) return; // the job said it can never succeed

        await alerts.SendAsync($"Job {context.Job.TypeName}.{context.Job.MethodName} ({context.Job.Id}) failed " +
                               $"after {context.Attempt} attempts: {context.Exception.Message}", cancellationToken);
    }
}
```

```csharp
options.WithJobFailureHandler<AlertOnFailure>();
```

It's called after the failure is saved in the history, and after the job's place on the worker is freed, in a scope of its own, so it
can't change the job's outcome and a slow alert doesn't hold up other jobs. Its exceptions are logged, and the next handler is still
called. The handlers of a job get 30 seconds in all, then their cancellation token is cancelled. During the shutdown, the app may stop
before they finish. A handler isn't called if the failure couldn't be saved: the job then runs again when its lease expires.

The context has the job (`Job`, as its last attempt was claimed: its row is gone, so don't attach it to a DbContext), what its last
attempt threw (`Exception`), the attempt (`Attempt`), whether the job cancelled itself (`IsCancelledByJob`), and the handlers' scope
(`Services`).

The metric `xactjobs.job.attempts` counts these jobs too, with the outcome `failed`: see [Metrics, traces and logs](../observability/).
