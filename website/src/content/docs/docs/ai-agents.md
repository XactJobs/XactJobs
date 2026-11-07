---
title: Using XactJobs with AI agents
description: A snippet for CLAUDE.md or AGENTS.md with the rules a coding agent needs to write XactJobs jobs correctly, and the llms.txt files.
---

Coding agents write jobs the way they've seen background jobs written elsewhere, and the one habit that breaks XactJobs' guarantees is
calling `SaveChanges` inside a job. Paste this into your project's `CLAUDE.md`, `AGENTS.md` or similar instructions file:

```markdown
## Background jobs (XactJobs)

- A job is a unit of work. Changes a job makes through the injected `TDbContext` are saved by the worker
  when the job finishes, in the same transaction that removes the job and records its history. This is
  what makes them exactly once.
- Don't call `SaveChanges` in a job, or call it only inside a transaction the job opens
  (`Database.BeginTransactionAsync`) and doesn't commit: the worker commits it. Anything saved otherwise
  (also `ExecuteUpdate`, `ExecuteDelete`, raw SQL, or another DbContext) is permanent right away, even if
  the job then fails, and happens again if the job runs again.
- Returning normally saves the pending changes. To stop without saving, throw. Any exception discards
  the changes and retries the job. If the job can never succeed (e.g. its data is gone), throw
  `XactJobCancelException`: nothing is saved and the job isn't retried.
- Calls to other systems (payments, email, HTTP APIs) are at least once. Send an idempotency key: pass it
  as a job argument (`$"charge-order-{orderId}"`, or `Guid.NewGuid()`, which is evaluated once, when the
  job is enqueued), or for periodic jobs build it from `XactJobContext.OriginallyScheduledAt`.
- Enqueue jobs on the same DbContext as the business data they belong to (`JobEnqueue`, `JobScheduleIn`,
  `JobScheduleAt`), and let the app's own `SaveChanges` or transaction commit both together.
- Job methods are public, not generic, not `async void`, without `out`/`ref` or delegate parameters.
  Pass `CancellationToken.None` and `null` for `CancellationToken` and `XactJobContext` parameters when
  enqueueing: the worker fills them in.
- Put long-running jobs in their own queue (`QueueNames.LongRunning` with `WithLongRunningQueue()`, or
  `WithIsolatedQueue`), so they don't occupy the slots of short jobs.
```

The rules come from [A job is a unit of work](../unit-of-work/), [Failures and retries](../failures-and-retries/),
[Idempotent external calls](../idempotent-external-calls/) and [Queues](../queues-and-polling/).

## llms.txt

The site publishes its docs for language models, generated from these pages at build time:

- [llms.txt](../../llms.txt): a short index of the docs pages, with a line on each
- [llms-full.txt](../../llms-full.txt): all the docs pages as plain markdown, in one file
