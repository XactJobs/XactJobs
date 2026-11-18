---
title: Guarantees and limitations
description: What XactJobs guarantees and what it doesn't, with the tests that show it on every database, and the versions it supports.
---

What XactJobs promises, what it doesn't, and the tests that check each promise. The database tests run on every pull request against
PostgreSQL 16, 17 and 18, SQL Server, MySQL (both EF providers), Oracle and SQLite, on .NET 9 and 10.

## Guarantees

| Guarantee | What it means | Shown by |
|---|---|---|
| **A job is saved with your data** | `JobEnqueue` adds the job to your `DbContext`: it's saved by your `SaveChanges`, in your transaction. If that rolls back, the job never existed. | [`JobEnqueuedInATransactionThatRollsBack_NeverExists_AndOneThatCommitsRuns`][integration] |
| **A job's database changes happen exactly once** | Changes made through the job's scoped `DbContext`, and not saved by the job (or saved in a transaction it doesn't commit), commit with the delete of the job's row and its history row. If two workers run the same job, only one can commit. See [A job is a unit of work](../unit-of-work/). | [`SuccessfulJob_CommitsItsWritesWithTheHistoryRow`, `JobThatLostItsRow_LosesItsWrites`, `JobThatLostItsRow_LosesWritesSavedInItsOwnOpenTransaction`, `CancelledJob_IsNotRetried_AndLosesItsWrites`][integration] |
| **Every job runs, until it completes or fails for good** | A job that throws is retried by the retry strategy; one that runs out of retries, or throws `XactJobCancelException`, moves to the history as cancelled. | [`SqliteRetryTests`][retry], [`ParallelFailures_EachGetErrorCountHistoryAndReschedule`][integration] |
| **Competing workers don't share a job** | A worker claims a job with a lease, and the claim skips the jobs leased by others. Many workers on many nodes run each job once. | [`CompetingWorkers_RunEveryJobExactlyOnce`][integration] |
| **A running job keeps its lease** | The worker extends the lease while the job runs, so no other worker takes it, however long it runs. | [`RunningJob_KeepsItsLeaseExtended_AndNoOtherWorkerClaimsIt`][integration] |
| **A crashed worker's jobs run again** | A worker that dies (killed, its machine lost) leaves its jobs leased: another worker runs them once the lease expires (`WithLeaseDuration`, 120 s by default). The crashed attempt isn't counted as a failed one. The same holds for a claim whose response is lost: it committed, but the connection failed before the jobs came back. | [`JobOfAWorkerThatDied_RunsOnceOnAnotherWorker_AfterItsLeaseExpires`][integration] |
| **The workers survive the database going away** | When the database ends their connections (a restart, a failover), the workers log, retry, and go on: every job completes once. A failed job's attempt is recorded on a new connection, tried again a few times; if it still can't be, the job runs again when its lease expires. | [`SessionsEndedByTheDatabaseDuringABacklog_EveryJobCompletes_AndItsWritesCommitOnce`][integration] |
| **A stop doesn't lose jobs** | Running jobs get `WithShutdownTimeout` to finish, then their token is cancelled; the leases of the unfinished ones are cleared, so they run again right away, without counting as failed attempts. | [`Stop_LetsRunningJobsFinishWithinTheTimeout`, `Stop_ClearsLeasesOfRunningJobs_ButNotOfOtherLeasers`][integration] |
| **A periodic job has one pending run** | A unique index holds at most one pending run per periodic job and version, also when several nodes ensure it at once, so runs never overlap. | [`SecondRunOfAVersion_IsRefused`][periodic-index], [`TwoNodesChangingADefinitionAtOnce_LeaveOneLiveRow`][integration] |
| **A continuation waits for its whole batch** | A continuation starts only once none of its batch's jobs is left, and a retry moving inside the batch can't make the batch look finished. | [`RetriedJobs_NeverLetTheContinuationStartEarly`, `CompetingWorkers_RunEachContinuationExactlyOnce`][continuations] |
| **The workers don't block your app's writes** | Claiming jobs doesn't block inserting new jobs or deleting running ones, and works on connections your app left at another isolation level. | [`ClaimJobs_BlocksNeitherAnInsertNorTheDeleteOfARunningJob`, `Claims_OnConnectionsLeftAtSerializable_Succeed`][integration] |

## What isn't guaranteed

- **A job can run more than once.** Its database changes happen once, but its code can run again: after a crash, after a lease expired
  while it was running (e.g. its node lost the database for longer than the lease), or after a commit whose connection failed. Make calls
  to other systems idempotent: see [Idempotent external calls](../idempotent-external-calls/).
- **Changes saved outside the job's transaction aren't exactly once**: what the job writes outside a transaction it leaves open
  (`SaveChanges`, `ExecuteUpdate`, `ExecuteDelete` or raw SQL without one), what it commits itself, and what it writes through other
  `DbContext` instances. They're permanent at once, also if the job then fails. The save
  check warns about the first kind ([the save check](../unit-of-work/#the-save-check)). Shown by
  [`JobThatLostItsRow_KeepsWritesItSavedWithoutATransaction`][postgres-transactions].
- **A commit whose connection fails has an unknown outcome**, for any database client. If it committed, the worker finds the job's row gone
  and logs a warning; if it didn't, the job runs again. Its database changes are still once, its other effects maybe twice.
- **No order between jobs.** Workers claim the oldest due jobs first, but run them in parallel, on many nodes: a job can finish before an
  older one, and a failed job's retry comes after the jobs behind it. For an order, use [continuations](../batches-and-continuations/).
- **Not to the second.** A job runs at the first poll after its time (`WithPollingInterval`, 6 s by default), or right away with
  [QuickPoll](../queues-and-polling/#quickpoll) in the same process. Whether a job is due, and whether a lease has expired, is decided by
  the database's clock; scheduled and retry times come from the application's clock. Keep the clocks in sync (NTP).
- **Missed periodic runs aren't caught up.** After downtime, a periodic job's overdue run runs once, and the next one is scheduled from
  then: the times in between are skipped. There are no misfire policies.
- **The history is kept until you delete it.** With `WithHistoryRetentionPeriodInDays`, older rows are deleted every hour.

## Limitations

- **EF Core only, and your database only.** The jobs live in your database, next to your data: that's the point. There's no Redis or
  other storage, and no support for apps without EF Core.
- **No lock per job.** Two jobs that must not run at the same time can lock the rows they work on, in the job's transaction. A periodic
  job never overlaps itself.
- **Scheduling is cron and one-off times.** Cron with seconds, time zones, `L`/`W`/`#`, and [exclusions](../periodic-jobs/#exclusions);
  not Quartz's calendar intervals, daily windows or misfire policies.
- **No priorities within a queue.** Use the priority queue or isolated queues, each with its own workers.
- **Before 1.0, the API can still change** between minor versions. Breaking changes are listed in the
  [release notes](https://github.com/XactJobs/XactJobs/releases) and in [Upgrading](../upgrading/), with how to migrate.

## Supported versions

| | Supported | Tested on every pull request |
|---|---|---|
| .NET | 8, 9, 10 | 9 and 10 (the library also builds for 8) |
| EF Core | 9 (on .NET 8 and 9), 10 (on .NET 10) | 9 and 10 |
| PostgreSQL | as your EF provider supports | 16, 17, 18 |
| SQL Server | as your EF provider supports | 2022 |
| MySQL | 8.0 or later (not MariaDB) | 8.4, with MySql.EntityFrameworkCore, and Pomelo on EF Core 9 |
| Oracle | 12c or later | 23 Free |
| SQLite | as your EF provider supports | Microsoft.Data.Sqlite |

A new .NET or EF Core version is added when it's released. A version is dropped only when Microsoft no longer supports it, in a minor
version, announced in the release notes. The database images the tests use have exact versions, so every run tests the same ones.

[retry]: https://github.com/XactJobs/XactJobs/blob/main/src/XactJobs.Tests/SqliteRetryTests.cs
[integration]: https://github.com/XactJobs/XactJobs/blob/main/src/XactJobs.Tests/Databases/DatabaseIntegrationTests.cs
[periodic-index]: https://github.com/XactJobs/XactJobs/blob/main/src/XactJobs.Tests/PeriodicRunIndexScenarios.cs
[continuations]: https://github.com/XactJobs/XactJobs/blob/main/src/XactJobs.Tests/Databases/ContinuationIntegrationTests.cs
[postgres-transactions]: https://github.com/XactJobs/XactJobs/blob/main/src/XactJobs.Tests/Postgres/PostgresJobTransactionTests.cs
