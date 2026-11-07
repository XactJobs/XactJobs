---
title: A job is a unit of work
description: The worker saves a job's database changes in the same transaction that marks it completed, so they happen exactly once. The save check warns when a job saves outside a transaction.
---

**The worker saves a job's database changes, not the job.** Changes a job makes through the injected `TDbContext` are saved when the
job finishes, in the same transaction that marks it as completed:

- **Exactly once.** If a lease expires and another worker runs the job again, only one of the runs can commit, so the changes happen once.
- **No transaction stays open while the job runs**, unless the job opens one. The changes wait in EF's change tracker, and are written at
  the end in one short transaction.
- **Don't call `SaveChanges` in a job**, or call it only inside a transaction you open and don't commit: the worker commits it. Changes
  saved any other way are permanent right away, even if the job then fails.
- **Returning saves.** Returning normally, even early, saves the pending changes and records the job as completed. A job that wants to
  stop without saving must throw instead.
- **Throwing discards.** Any exception discards the pending changes, and the job is retried. To stop a job that can never succeed, e.g.
  because the data it works on is gone, throw `XactJobCancelException`: its changes are discarded too, but it isn't retried, and it moves
  to the history as cancelled, with the exception's message.

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

## Exactly-once database effects

A lease can expire while a job is still running, for example if a node loses its database connection for a while. Another worker can
then run the same job. For the database writes this is safe, as long as the job uses the scoped `TDbContext` injected into its class and
does one of these two things:

- **Doesn't save its changes.** The worker saves them when the job finishes, as in the example above.
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

Either way the job's writes, the removal of the job row and the new history row all commit in one transaction. If two workers run the
same job, only one of them can commit: the other one's delete affects 0 rows, EF throws a concurrency exception, and its writes roll back.
The same happens if the job fails.

**This doesn't apply to** changes the job saves without a transaction, or commits itself, because they are already permanent when the job
finishes. It also doesn't apply to other DbContext instances, or to external side effects like sending emails or calling HTTP APIs. These
can happen more than once, so they should be idempotent: see [Idempotent external calls](../idempotent-external-calls/).

## The save check

**The worker warns when a job saves without a transaction.** While a job runs, the worker checks the saves on the scoped `TDbContext` it
created for the job. If the job calls `SaveChanges` without a transaction, the worker logs a warning that names the job, once per job
method, and the save goes ahead.

`WithSaveOutsideTransaction` changes this, for all the queues:

```csharp
builder.Services.AddXactJobs<UserDbContext>(options =>
{
    options.WithSaveOutsideTransaction(XactJobSaveCheck.Throw);
});
```

| Value | When a job saves without a transaction |
|---|---|
| `XactJobSaveCheck.Warn` (default) | a warning is logged, once per job method, and the save goes ahead |
| `XactJobSaveCheck.Throw` | `SaveChanges` throws instead, so nothing is saved, and the job fails and is retried as after any other exception |
| `XactJobSaveCheck.Ignore` | no check |

Only `SaveChanges` on that `TDbContext` is checked: not other DbContext instances, not `ExecuteUpdate`, `ExecuteDelete` or raw SQL, which
also run right away without a transaction, and not a transaction the job commits itself.
