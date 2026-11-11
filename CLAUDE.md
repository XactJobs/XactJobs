# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build Commands

```bash
# Build solution
dotnet build src/XactJobs.sln

# Build specific framework
dotnet build -f net8.0 src/XactJobs.sln
dotnet build -f net9.0 src/XactJobs.sln
dotnet build -f net10.0 src/XactJobs.sln

# Release build
dotnet build -c Release src/XactJobs.sln

# Build and create the NuGet package (GeneratePackageOnBuild is on, and dotnet pack would not build)
dotnet build -c Release src/XactJobs/XactJobs.csproj
```

## Test Commands

```bash
# Run all tests
dotnet test src/XactJobs.sln

# Run on one target framework
dotnet test -f net10.0 src/XactJobs.Tests/XactJobs.Tests.csproj

# Run single test class
dotnet test --filter "FullyQualifiedName~SqliteDatabaseIntegrationTests" src/XactJobs.Tests/XactJobs.Tests.csproj

# Run with verbose output
dotnet test --verbosity=detailed src/XactJobs.Tests/XactJobs.Tests.csproj

# Run with code coverage
dotnet test --collect:"XPlat Code Coverage" src/XactJobs.Tests/XactJobs.Tests.csproj
```

The dashboard is tested in `src/XactJobs.UI.Tests/` (SQLite and an in-memory test server), and its queries and actions on every database in `src/XactJobs.Tests/Databases/DashboardIntegrationTests.cs`.

The tests in `src/XactJobs.Tests/Postgres/` and `src/XactJobs.Tests/Databases/` run against real databases in containers (Testcontainers), and are skipped if Docker isn't running. `Databases/` is one suite that runs on PostgreSQL, MySQL, SQL Server and Oracle. PostgreSQL runs as 16, 17 and 18, each with its own container, fixture (`Postgres16Fixture`...) and test classes (`Postgres16IntegrationTests`...), in `Postgres/` too. The first run downloads the images (about 5 GB). The images have exact versions (in `Postgres/PostgresFixture.cs` and `Databases/DatabaseFixture.cs`), so every machine tests the same ones: a moving tag (`mysql:8`) stays at the version a machine pulled first, and query plans differ between versions. Update them on purpose, and run the suites of that database.

On a small machine (e.g. 8 GB of RAM), run them one framework and one database at a time: `dotnet test` runs both frameworks at once, and xUnit runs the database classes in parallel, which starts several SQL Server and Oracle containers together, and they fail to start or time out.

```bash
dotnet build src/XactJobs.Tests/XactJobs.Tests.csproj
dotnet test --no-build -f net9.0 src/XactJobs.Tests/XactJobs.Tests.csproj --filter "FullyQualifiedName!~XactJobs.Tests.Databases&FullyQualifiedName!~XactJobs.Tests.Postgres"
# then each PostgreSQL version of Postgres/: Postgres16, Postgres17, Postgres18
dotnet test --no-build -f net9.0 src/XactJobs.Tests/XactJobs.Tests.csproj --filter "FullyQualifiedName~XactJobs.Tests.Postgres.Postgres16"
# then each database of Databases/: Postgres16, Postgres17, Postgres18, OfficialMySql, PomeloMySql (net9.0 only), SqlServer, Oracle
# (the prefix runs both suites of the database: its *IntegrationTests and *DashboardTests)
dotnet test --no-build -f net9.0 src/XactJobs.Tests/XactJobs.Tests.csproj --filter "FullyQualifiedName~XactJobs.Tests.Databases.Oracle"
```

Then the same with `-f net10.0`.

## Benchmark Commands

`src/XactJobs.Benchmarks/` (see its README) measures XactJobs' own code, to compare commits on the same machine: `micro` (BenchmarkDotNet, no database) and `drain` (the worker on SQLite in memory, or on PostgreSQL, SQL Server or MySQL, compared with the same database work in plain EF). Release builds only, and like the tests, one run at a time on a small machine.

```bash
dotnet build -c Release src/XactJobs.Benchmarks/XactJobs.Benchmarks.csproj
dotnet run --no-build -c Release -f net10.0 --project src/XactJobs.Benchmarks -- micro --filter "*" --job short --inProcess
dotnet run --no-build -c Release -f net10.0 --project src/XactJobs.Benchmarks -- drain --jobs 3000 --runs 5
```

The library has `InternalsVisibleTo` for the benchmarks, which call the serializer, the compiler and the save check directly.

## Website

`website/` (see its README) is the landing page and the docs (Astro and Starlight), deployed to GitHub Pages by `.github/workflows/website.yml` on pushes to main that change it. Node.js 22.23 or later.

```bash
cd website && npm ci && npm run build
```

The docs pages (`website/src/content/docs/docs/`) follow the README and the code: a change to the API, an option, a default or a SQL script updates them too. Their code samples must compile against the current API.

## Code Quality

- `TreatWarningsAsErrors` is enabled - all warnings fail the build
- Threading analyzer (VSTHRD111) is enforced as error - async methods must return Task, not void
- Nullable reference types are enabled

## Architecture Overview

XactJobs is a .NET library for background job processing with full database transaction support via Entity Framework Core. Jobs are stored in the same database as business data and only execute if the transaction commits.

### Key Components

**Public API** (`src/XactJobs/`)
- `DbContextExtensions.cs` - Main API: `JobEnqueue()`, `JobScheduleAt()`, `JobScheduleIn()`, `JobEnsurePeriodic()`, `JobBatch()`
- `XactJobBatch.cs`, `XactJob.ContinueWith()` - Fluent API for batches and continuations (see below)
- `QuickPoll.cs` - Channel-based immediate job execution without waiting for poll interval
- `XactJobsOptionsBuilder.cs` - Fluent configuration for queues, workers, and periodic jobs

**DependencyInjection/**
- `ServiceCollectionExtensions.cs` - `AddXactJobs<TDbContext>()` registration
- `XactJobsRunnerDispatcher.cs` - Background service orchestrating worker threads
- `XactJobsCronScheduler.cs` - Cron expression scheduling for recurring jobs

**Internal/**
- `XactJobRunner.cs` - Core polling loop and job execution. A worker holds (claims, leases) at most `BatchSize` jobs, by default twice `MaxDegreeOfParallelism`, and runs `MaxDegreeOfParallelism` of them at once (`_runSlots`). With a batch larger than the parallelism, during a backlog it claims again only when fewer jobs wait than it can run, and then waits for the batch to finish, at most `EarlyClaimDelay` (1 s), so that the claim doesn't run during the jobs' saves (on SQLite, a write that collides with another waits 150 ms in Microsoft.Data.Sqlite's retry); at or below it, a worker only claims jobs it can start. A job of a batch counts as running once it has a slot, and doesn't start if the worker is stopping; it stays in flight, so its lease is cleared on shutdown
- `XactJobSerializer.cs` - Lambda expression serialization: the job's type, its method and its arguments as JSON, the strings a job is stored as. They must never change (existing rows, and periodic jobs are compared with their definition by them): `Golden/SerializerFormatTests` has the exact strings. Common argument expressions are evaluated without compiling them, the others with `ExpressionUtil/CachedExpressionCompiler`. The method name is stored with its parameter types, e.g. `Send(System.String, System.Int32)`, so overloads can be jobs. To run a job, the runner looks for the overload with that signature, or, if there isn't one, the only method with that name and parameter count (jobs from before 0.1.7 have no signature)
- `XactJobCompiler.cs` - Expression compilation to executable delegates
- `SqlDialects/` - Database-specific SQL (SqlServer, PostgreSQL, MySQL, Oracle, SQLite). Oracle claims in one PL/SQL block that limits the rows on the server (`BULK COLLECT ... LIMIT`): `ROWNUM` is applied before locked rows are skipped, and ODP.NET's prefetch ignores `FetchSize` on a statement's first execution on a connection, so limiting the rows read on the client still locks most of a backlog

**EntityConfigurations/**
- EF Core entity configurations for XactJob, XactJobPeriodic, XactJobHistory tables

**XactJobs.UI** (`src/XactJobs.UI/`, Razor Class Library)
- Dashboard pages (`Areas/XactJobs/Pages/`) with htmx: each list page serves its table on its own (`?handler=Table`), and reloads it when an API action answers with the `xactjobs-changed` event (`HX-Trigger` header)
- `Services/XactJobsUIService.cs` - queries and actions. Actions must follow the core's rules: never update a job row in place (the scheduled time is part of the primary key) nor touch a leased row, and never add a second pending run to a periodic job (the unique index refuses it: Trigger and Resume answer a conflict, caught with `IsUniqueKeyViolation`)
- `Api/XactJobsApiEndpoints.cs` - REST API, access (local requests only by default, or an authorization policy) and antiforgery checks
- `Assets/` - the dashboard's scripts, styles and fonts, embedded in the assembly and served by `Api/XactJobsAssets.cs` under `{BasePath}/assets/`, with the pages' access and a content hash in the URL (not a `wwwroot`: static web assets would need the static files middleware, and only work unpublished in Development)
- Light and dark theme: `Assets/js/xactjobs-theme.js`, loaded first in the layout's head (not deferred), sets Bootstrap's `data-bs-theme` from the navbar switch (localStorage) or `prefers-color-scheme`. Use Bootstrap's theme-aware classes and CSS variables (`bg-body-secondary`, `var(--bs-tertiary-bg)`), not fixed light colors (`bg-light text-dark`); the dark adjustments are at the end of `xactjobs.css`, and `xactjobs.js` gives the charts the theme's colors. The website shows `docs/images/<name>-dark.png` in its dark theme, so a new screenshot needs both
- `Assets/lib/` - Bootstrap, Bootstrap Icons, Chart.js and htmx, copied from their npm packages (source map comments removed), so the dashboard needs no CDN. Times are rendered as UTC `<time>` elements and shown in the browser's time zone by `Assets/js/xactjobs.js`
- Run now moves the row to now with one `UPDATE` by its whole key (the scheduled time is part of it, and EF can't change a tracked entity's key), only if it isn't leased: the job keeps its id, `BatchId`, `AwaitsBatchId` and `ContinuationCondition`. Cancel moves it to the history (insert, then delete, in one transaction)
- A pending job is found by its whole primary key (`JobKey`: queue, scheduled time, id; the API takes the queue and the time in the query string), never by its id alone: on PostgreSQL, SQL Server and Oracle the key is `(queue, scheduled_at, id)` and there's no index on the id. Other lookups of job rows go through an index too (the batch columns, the periodic job column): `QueryPlanScenarios` checks the plans on every database
- History categories: `Failed` in the history is an attempt the runner already rescheduled ("Retried"); a job that ran out of retries, or threw `XactJobCancelException`, is `Cancelled` with a stack trace ("Failed"); a job cancelled from the dashboard is `Cancelled` without one

### Batches and Continuations

- A batch is the jobs with the same `BatchId`, and is finished when none of them is left in the job table. There's no batch table. A job continued on its own gets a batch of one
- A continuation waits as a job row with `AwaitsBatchId`, its `ContinuationCondition`, and the reserved `ScheduledAt` 9999-12-31 (`XactJobBase.AwaitingBatchScheduledAt`), so the poll for due jobs never reaches it. Its retries and history rows keep `AwaitsBatchId`, but have real scheduled times
- Each worker first claims the continuations of its queue whose batch is finished (`ISqlDialect.ClaimContinuationsAsync`), then due jobs with the slots left. Continuations are claimed at most once per polling interval, or right away when the worker that finished a batch wakes the queue (`QuickPollChannel.NotifyContinuationsReady`)
- The claimed continuation's worker counts the batch's failed jobs in the history (`Cancelled` or `Skipped`; `Failed` attempts were retried), and either runs the continuation or moves it to the history as `Skipped`
- The check for the batch's jobs must not skip locked rows (no `SKIP LOCKED`/`READPAST` in the subquery). On SQL Server it also takes `HOLDLOCK` (a range lock on the batch in its filtered index, until the claim commits): locking READ COMMITTED (without snapshot isolation) reads row by row, and could miss a row that a retry or Run now moves (an `UPDATE` of the key) to a place the scan already passed, and the batch would look finished. Inserting a replacement before deleting the old row only made that window smaller
- The history cleanup keeps the history of batches that a waiting continuation needs
- Dashboard: the Batches page groups the job table and the history by `BatchId` (active: jobs left; finished: history only). Waiting continuations are shown by `JobDto.IsAwaitingBatch`, never by their placeholder date

### Retries

- A retry is the same row: `RescheduleAsync` moves it to its retry time with an `UPDATE` by its whole old key (`ExecuteUpdate`, EF can't change a tracked entity's key), setting the error count and the original time and clearing the lease, in the transaction of `RecordFailureAsync` that adds the history row (a cleared context: the failed job's changes are discarded). No row updated means another worker processed it after its lease expired: `DbUpdateConcurrencyException`, logged as a warning
- So a job keeps its id across its attempts: the history has a row per attempt (primary key: id and processed time, on SQLite too since v0.6.0), and lookups of a history row by id take the last one
- The success path (`RunJobAsync`) never starts a transaction of its own: the job may have opened one, or have unsaved changes, and a retrying execution strategy can't re-run the job

### Original Scheduled Time

- `XactJobBase.OriginallyScheduledAt`: the time a job was originally scheduled for, null when it's the same as `ScheduledAt`. Retries (`RescheduleAsync`), the dashboard's Run now (and Trigger, which uses it) and the dashboard's Retry from the history set it, the history rows copy it. Cron slots are whole seconds (`CronSequenceGenerator` drops the whole fraction of the current second)
- `GetOriginallyScheduledAt(claimedAt)` gives the effective value: for a continuation's first attempt, which waits at the reserved time, it's the time of its first claim. Never copy 9999-12-31
- The continuation claim saves it: the statement that leases the rows (every dialect's `ClaimContinuationsAsync`) sets `originally_scheduled_at = COALESCE(originally_scheduled_at, claimedAt)`, with the runner's `claimedAt` as a parameter (in whole microseconds, so every database stores it exactly), not the database clock. So a second run of the first attempt, after its lease expired, gets the same value. PostgreSQL, SQL Server and SQLite return the rows after the lease; MySQL's locking select and Oracle's result cursor return them as they were before it, with the value of an earlier claim (committed), or null on a first claim, when the worker's `claimedAt` is the value just saved. The claims of regular jobs don't set it. The column isn't indexed, so the PostgreSQL claim stays a HOT update
- History rows get it only when it differs from their own scheduled time
- Jobs get it as `XactJobContext.OriginallyScheduledAt`: a job method can take an `XactJobContext` parameter, which `XactJobSerializer` stores as null and `XactJobCompiler` fills in, like a `CancellationToken`

### Periodic Job State

- `XactJobPeriodic.State` (`state`, v0.6.0, at most `MaxStateLength` = 2000 characters, the most Oracle's `NVARCHAR2` holds): what the last completed run left for the next one. It belongs to the id: `JobEnsurePeriodic`/`WithPeriodicJob` and pausing never change it, deleting the periodic job deletes it. Not on the job rows: there's one pending run per periodic job, and the state is saved in the transaction that deletes the run's row
- Jobs get it in `XactJobContext.PeriodicState`, from the periodic row loaded at the claim, and set it with `SetPeriodicState` (throws outside a periodic run, or above the limit). The runner copies it to the attached periodic row only if the run completes and the value changed, also when the periodic job was paused or its definition changed meanwhile; if it was deleted, it's dropped. On a failure, the periodic instance gets its state back (`RunJobAsync`), since the failure is recorded with it
- The history has `previous_state` and `state` on every attempt: the state the run started with, and the one after it (the same unless it completed and set one)
- The dashboard's Retry of a history row with a state is refused: it runs as a one-off job, which has no state. `PeriodicStateScenarios` runs on every database

### Saves Outside a Transaction

- While a job runs, `XactJobRunner` subscribes `XactJobSaveChecker` to the `SavingChanges` event of the scoped `TDbContext` it created, and unsubscribes in a `finally` before its own save, which must never be checked
- `Database.CurrentTransaction` is null in the handler when the job didn't open a transaction: EF starts its own after the event (tested on every database in `SaveCheckScenarios`)
- `XactJobSaveCheck` (`WithSaveOutsideTransaction`, all the queues): `Warn` logs once per job type and method, per dispatcher (the runners share one checker); `Throw` throws `InvalidOperationException` from the event, so nothing is saved and the job is retried; `Ignore` doesn't subscribe. In `Warn` mode a job method that was already warned about isn't subscribed again
- Apps may register their context with `AddDbContextPool`: the runner must give a pooled context back as it got it (EF resets `AutoTransactionBehavior`; the save check's handler is removed in the `finally`). `SqlitePooledContextTests` checks both
- Only `SaveChanges` on that context is seen: not other DbContext instances, `ExecuteUpdate`/`ExecuteDelete`, raw SQL, or a transaction the job commits itself

### Schema of XactJobs' Tables

- PostgreSQL and SQL Server only: `ApplyXactJobsConfigurations(..., schema: "app_b")`, for apps that share a database. The dialects take it in their constructor and write it into their SQL, so it must be a plain identifier (lowercase on PostgreSQL, whose unquoted names fold to lowercase while EF quotes them)
- The runtime finds it in the model: `DbContext.GetSqlDialect()` (never `ProviderName.ToSqlDialect()` alone, which gives the default schema). `SchemaIntegrationTests` runs two apps' tables in one database

### SQL Scripts

- `sql/v<version>/xact-jobs-<database>-v<version>.sql` - one per schema change and database, to upgrade an installation. Released ones never change
- `sql/install/xact-jobs-<database>.sql` - the whole latest schema, for a new installation. A schema change updates it too, and its version in the header: `InstallScriptIntegrationTests` (and `SqliteInstallScriptTests`) check on every database that it creates the same schema as the versioned scripts
- The EF model must match them too (the EF migrations route): `ModelIndexTests`, and `SchemaGeneratedFromTheModel_IsAcceptedByTheDatabase` on MySQL

### Periodic Job Scheduler

- `XactJobsCronScheduler` ensures the periodic jobs from the options at start, and reconciles them every interval, in a transaction that holds a lock (`ISqlDialect.AcquireTableLockAsync`), so that nodes don't do it at the same time
- The lock ends with the transaction on PostgreSQL and Oracle (`LOCK TABLE`) and SQL Server (`sp_getapplock`), and SQLite needs none. MySQL's `GET_LOCK` belongs to the session and to the whole server: it's named after the database (hashed, a name has at most 64 characters), and released in a `finally`, also when the cycle fails or is cancelled. Otherwise its connection goes back to the pool holding it (MySql.Data doesn't reset a pooled session; MySqlConnector only on reuse), and the other nodes' cycles wait 30 s for it and fail

### Indexes of the Job Table

- The claims, the leases and the worker's delete use the primary key: `(queue, scheduled_at, id)`, or on SQLite the id with `ix_job_queue_scheduled_at` (SQLite only generates ids for `INTEGER PRIMARY KEY`)
- `batch_id` and `awaits_batch_id`, for batches and continuations, in the job table and the history: like `periodic_job_id` below, only the rows where they're set (most jobs are in no batch), so the queries must imply that the column isn't null (an equality does). Oracle's provider gave one parameter compared with both columns (`AwaitsBatchId == id && !Any(BatchId == id)`) a type that made Oracle compare `RAWTOHEX(BATCH_ID)` and read the whole table: each column gets its own parameter. A `Contains` on a batch column also needs `column != null` beside it: EF Core 9 sends the list as a JSON parameter (`IN (SELECT ... FROM OPENJSON(...))`), from which SQL Server doesn't infer that the column isn't null, so it read the whole job table instead of the filtered index (EF Core 10 sends separate parameters). `QueryPlanScenarios.BatchQueries_UseTheBatchIndexes` checks the plans, on both frameworks
- `(periodic_job_id, periodic_job_version)`, unique (v0.6.0): one pending run per periodic job and version, and the lookups of a periodic job's runs. Only the rows of periodic jobs: a partial or filtered index (`IS NOT NULL`) on PostgreSQL, SQL Server and SQLite, a plain one on Oracle (which leaves out the rows where all its columns are null; the model clears the `IS NOT NULL` filter Oracle's provider gives a unique index of nullable columns). MySQL has no filtered indexes up to 9.7: a full one, written by every job, and the column is a `varchar(255)` there (a `longtext` can't be indexed). The v0.6.0 scripts give runs without a version (before v0.1.4) version 1, so the databases' different handling of nulls in a unique index doesn't matter, and fail on existing duplicates (no deleting)
- A completed run deletes its row and adds the next run in one `SaveChanges`: EF orders the deletes of a table before its inserts, which the unique index needs (a second worker after an expired lease then gets the `DbUpdateConcurrencyException` of its delete, as before). A run added at the same time outside of that (resume, `JobEnsurePeriodic` with a new version, the dashboard's Trigger, the reconciliation) fails with a unique key violation: `DbExceptionExtensions.IsUniqueKeyViolation` (public) knows each provider's error; the scheduler runs its cycle again right away, the dashboard answers a conflict. `PeriodicRunIndexScenarios` runs on every database
- A new index adds a write to every job's insert and delete, unless it's filtered. On PostgreSQL, a claim must not change an indexed column, so it stays a HOT update

### Shutdown

- Once the stopping token (or a job's token) is cancelled, an exception counts as that cancellation, not only an `OperationCanceledException`: SqlClient throws a `SqlException` ("Operation cancelled by user") for a cancelled command. So a stop doesn't log errors (the poll loop, the lease timer, the cron scheduler), and a job cancelled by it runs again without a failed attempt, unless it threw `XactJobCancelException`. `SqliteShutdownCancellationTests` simulates such a provider

### Metrics and Logs

- `Internal/XactJobsMetrics.cs`: the meter `XactJobs` (System.Diagnostics.Metrics, no OpenTelemetry dependency): attempts by outcome (completed, skipped, retried, failed, interrupted), their duration, the start delay (from the scheduled time, or a waiting continuation's claim), the claims' duration and jobs, and observable running and held jobs per queue, from the registered runners (`IXactJobsWorkerState`). Tags: `xactjobs.queue`, `xactjobs.outcome`, and with `WithJobTagsInMetrics` the job's type and method. Instrument names, units and tags are a contract, like log messages: dashboards depend on them
- No counter of saves outside a transaction: in `Warn` mode the checker stops watching a job method after its first warning, so it would undercount
- `Internal/XactJobsTracing.cs`: the activity source `XactJobs`: an activity per attempt (Consumer, named "ShortType.Method", a root: `Activity.Current` is cleared first), with messaging-convention and `xactjobs.*` tags, status Error and an `exception` event for retried and failed attempts. Started before the log scope, so log entries get its trace id
- `XactJobBase.TraceParent` (job and history, `trace_parent`, v0.5.0): the W3C traceparent of `Activity.Current` at enqueue (`XactJobSerializer.FromExpression`); each attempt's activity links to it (a link, not the parent). Retries, the history and the dashboard's Cancel, Run now and Retry copy it; periodic runs have none
- `Internal/XactJobLogScope.cs`: each attempt runs in a log scope with JobId, JobType, JobMethod, JobQueue and JobAttempt
- `SqliteObservabilityTests`: the meter is shared with the tests running at the same time, so each test has a queue of its own and filters the measurements by it

### Database Entities

- **XactJob** - Main job queue (status, payload, scheduled time, retry info)
- **XactJobPeriodic** - Recurring job definitions with version tracking
- **XactJobHistory** - Executed job records, kept unless `WithHistoryRetentionPeriodInDays` is set: then the periodic job `xj_history_cleanup` deletes the older ones every hour. Without it, the scheduler removes that job at start (older versions always added it, with 30 days)

### Job Execution Flow

1. Jobs enqueued via `DbContext.JobEnqueue()` → stored in XactJob table
2. `XactJobsRunnerDispatcher` starts worker threads per configured queue
3. `XactJobRunner` polls database, claims jobs via lease, deserializes and executes
4. On completion, job moved to history; on failure, retry with exponential backoff

### Multi-Queue Support

- Default queue (2 workers)
- Priority queue (optional, `WithPriorityQueue()`)
- Long-running queue (optional, `WithLongRunningQueue()`)
- Custom queues via `WithIsolatedQueue(name)`

## Target Frameworks

- net8.0, net9.0, net10.0 (library): EF Core 9 on net8.0 and net9.0, EF Core 10 on net10.0, because EF Core 10 only targets net10.0
- net9.0, net10.0 (tests), to cover both EF Core versions. MySQL runs with the official provider on both, and with Pomelo only on net9.0, because it doesn't support EF Core 10 yet

## Third-Party Code

XactJobs is MIT (`LICENSE`; before 0.3.0 it was LGPL-3.0-or-later). Its source files start with the two-line MIT header. Code from elsewhere must have a license that allows that, and keep its notice:

- `Internal/ExpressionUtil/`, `Internal/CachedExpressionCompiler.cs` - Copied from ASP.NET MVC (Apache 2.0)
- `CronUtil/CronSequenceGenerator.cs` - Modeled on the Spring Framework's CronSequenceGenerator (Apache 2.0)
- `XactJobs.UI/Assets/lib/` - Bootstrap, Bootstrap Icons, Chart.js, htmx (MIT, license files next to them)
