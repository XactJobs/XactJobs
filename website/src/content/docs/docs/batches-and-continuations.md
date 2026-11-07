---
title: Batches and continuations
description: Continue a job with another, group jobs in a batch, and run continuations OnSuccess, OnFailure or OnFinish.
---

## Continuations

A continuation is a job that starts when another job is finished. It can have continuations of its own:

```csharp
dbContext.JobEnqueue<Importer>(x => x.ImportAsync(fileId))
    .ContinueWith<Reports>(x => x.RebuildAsync())
    .ContinueWith<Mailer>(x => x.SendReportAsync());

dbContext.SaveChanges();
```

By default a continuation runs if the job succeeded. The condition can also be `OnFailure` or `OnFinish`, and a continuation can go to another queue:

```csharp
var import = dbContext.JobEnqueue<Importer>(x => x.ImportAsync(fileId));

import.ContinueWith<Reports>(x => x.RebuildAsync());
import.ContinueWith<Ops>(x => x.AlertAsync(fileId), XactJobContinuation.OnFailure, QueueNames.Priority);
```

| Condition | Runs when |
|---|---|
| `OnSuccess` (default) | all the jobs it waits for succeeded |
| `OnFailure` | at least one of them failed |
| `OnFinish` | they are all finished, whether they succeeded or not |

A job failed if it ran out of retries, threw `XactJobCancelException`, was cancelled from the dashboard, or was a continuation that was
skipped, so in a chain of `OnSuccess` continuations, a failure skips the rest of the chain. A continuation whose condition isn't met moves
to the history as `Skipped`, with the reason as its message. Attempts that were retried don't count: a job of a batch that succeeds on its
second attempt succeeded.

A job can only be continued before it's saved, because its row can't be changed afterwards. To continue jobs that are saved separately,
put them in a batch.

## Batches

A batch is a group of jobs whose continuations start when all of them are finished:

```csharp
var batch = dbContext.JobBatch()
    .Enqueue<InventorySync>(x => x.SyncAsync("leeds"))
    .Enqueue<InventorySync>(x => x.SyncAsync("reno"))
    .ScheduleIn<InventorySync>(x => x.SyncAsync("osaka"), TimeSpan.FromHours(2));

batch.ContinueWith<Reports>(x => x.RebuildAsync())
    .ContinueWith<Mailer>(x => x.SendReportAsync());

batch.ContinueWith<Ops>(x => x.AlertAsync(), XactJobContinuation.OnFailure);

dbContext.SaveChanges();
```

A batch has `Enqueue`, `ScheduleIn` and `ScheduleAt`, each with an optional queue. The jobs of a batch are saved together, with one
`SaveChanges`: once it's saved, a batch can't get more jobs. Continuations can be added at any time, also later by the batch's id
(`batch.Id`). If the batch is already finished, they start right away:

```csharp
dbContext.JobBatch(batchId)
    .ContinueWith<Audit>(x => x.LogAsync(batchId), XactJobContinuation.OnFinish);

dbContext.SaveChanges();
```

A batch id that doesn't exist is a finished batch without jobs, so its continuations run as if all of its jobs succeeded.

## Details

- A retry of a job stays in its batch, and a continuation waits for it. A job retried from the dashboard is a new job, outside of the batch.
- A job of a batch can't be continued on its own: continue the batch. Periodic jobs can't be in a batch, nor be continued.
- A skipped continuation can be retried from the dashboard: it runs as a new job, outside of any batch.
- The worker that finishes the last job of a batch wakes up the workers of the continuations' queues, if they run in the same process,
  and the continuations start as soon as a worker is free. Besides that, each worker checks for continuations of finished batches once
  per polling interval. The check is one query, which looks at every continuation of the queue that's still waiting, so keep it in mind
  with many thousands of them.
- A continuation that waits for its batch is stored with the scheduled time 9999-12-31, so the poll for due jobs never reaches it.
- With [QuickPoll](../queues-and-polling/#quickpoll), `JobBatch()` registers the jobs of the batch for the notification. Their
  continuations don't need it.
- The history cleanup, if you turn it on (see [Configuration](../configuration/)), keeps the history of the batches that continuations
  still wait for, since it tells if their jobs failed.

The dashboard's Batches page shows the active and finished batches, with their progress: see [Dashboard](../dashboard/).
