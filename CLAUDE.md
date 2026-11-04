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

The tests in `src/XactJobs.Tests/Postgres/` and `src/XactJobs.Tests/Databases/` run against real databases in containers (Testcontainers), and are skipped if Docker isn't running. `Databases/` is one suite that runs on PostgreSQL, MySQL, SQL Server and Oracle. The first run downloads the images (about 5 GB).

On a small machine (e.g. 8 GB of RAM), run them one framework and one database at a time: `dotnet test` runs both frameworks at once, and xUnit runs the database classes in parallel, which starts several SQL Server and Oracle containers together, and they fail to start or time out.

```bash
dotnet build src/XactJobs.Tests/XactJobs.Tests.csproj
dotnet test --no-build -f net9.0 src/XactJobs.Tests/XactJobs.Tests.csproj --filter "FullyQualifiedName!~XactJobs.Tests.Databases&FullyQualifiedName!~XactJobs.Tests.Postgres"
dotnet test --no-build -f net9.0 src/XactJobs.Tests/XactJobs.Tests.csproj --filter "FullyQualifiedName~XactJobs.Tests.Postgres."
# then each database of Databases/: Postgres, OfficialMySql, PomeloMySql (net9.0 only), SqlServer, Oracle
# (the prefix runs both suites of the database: its *IntegrationTests and *DashboardTests)
dotnet test --no-build -f net9.0 src/XactJobs.Tests/XactJobs.Tests.csproj --filter "FullyQualifiedName~XactJobs.Tests.Databases.Oracle"
```

Then the same with `-f net10.0`.

## Code Quality

- `TreatWarningsAsErrors` is enabled - all warnings fail the build
- Threading analyzer (VSTHRD111) is enforced as error - async methods must return Task, not void
- Nullable reference types are enabled

## Architecture Overview

XactJobs is a .NET library for background job processing with full database transaction support via Entity Framework Core. Jobs are stored in the same database as business data and only execute if the transaction commits.

### Key Components

**Public API** (`src/XactJobs/`)
- `DbContextExtensions.cs` - Main API: `JobEnqueue()`, `JobScheduleAt()`, `JobScheduleIn()`, `JobEnsurePeriodic()`
- `QuickPoll.cs` - Channel-based immediate job execution without waiting for poll interval
- `XactJobsOptionsBuilder.cs` - Fluent configuration for queues, workers, and periodic jobs

**DependencyInjection/**
- `ServiceCollectionExtensions.cs` - `AddXactJobs<TDbContext>()` registration
- `XactJobsRunnerDispatcher.cs` - Background service orchestrating worker threads
- `XactJobsCronScheduler.cs` - Cron expression scheduling for recurring jobs

**Internal/**
- `XactJobRunner.cs` - Core polling loop and job execution
- `XactJobSerializer.cs` - Lambda expression serialization (adapted from Hangfire)
- `XactJobCompiler.cs` - Expression compilation to executable delegates
- `SqlDialects/` - Database-specific SQL (SqlServer, PostgreSQL, MySQL, Oracle, SQLite)

**EntityConfigurations/**
- EF Core entity configurations for XactJob, XactJobPeriodic, XactJobHistory tables

**XactJobs.UI** (`src/XactJobs.UI/`, Razor Class Library)
- Dashboard pages (`Areas/XactJobs/Pages/`) with htmx: each list page serves its table on its own (`?handler=Table`), and reloads it when an API action answers with the `xactjobs-changed` event (`HX-Trigger` header)
- `Services/XactJobsUIService.cs` - queries and actions. Actions must follow the core's rules: never update a job row in place (the scheduled time is part of the primary key) nor touch a leased row, and never add a second pending run to a periodic job
- `Api/XactJobsApiEndpoints.cs` - REST API, access (local requests only by default, or an authorization policy) and antiforgery checks
- `wwwroot/lib/` - Bootstrap, Bootstrap Icons, Chart.js and htmx, copied from their npm packages (source map comments removed), so the dashboard needs no CDN. Times are rendered as UTC `<time>` elements and shown in the browser's time zone by `wwwroot/js/xactjobs.js`
- History categories: `Failed` in the history is an attempt the runner already rescheduled ("Retried"); a job that ran out of retries is `Cancelled` with a stack trace ("Failed"); a job cancelled from the dashboard is `Cancelled` without one

### Database Entities

- **XactJob** - Main job queue (status, payload, scheduled time, retry info)
- **XactJobPeriodic** - Recurring job definitions with version tracking
- **XactJobHistory** - Executed job records (30-day default retention)

### Job Execution Flow

1. Jobs enqueued via `DbContext.JobEnqueue()` → stored in XactJob table
2. `XactJobsRunnerDispatcher` starts worker threads per configured queue
3. `XactJobRunner` polls database, claims jobs via lease, deserializes and executes
4. On completion, job moved to history; on failure, retry with exponential backoff

### Multi-Queue Support

- Default queue (2 workers)
- Priority queue (optional, `WithPriorityQueue()`)
- Long-running queue (optional, `WithLongRunningQueue()`)
- Custom queues via `WithCustomQueue()`

## Target Frameworks

- net8.0, net9.0, net10.0 (library): EF Core 9 on net8.0 and net9.0, EF Core 10 on net10.0, because EF Core 10 only targets net10.0
- net9.0, net10.0 (tests), to cover both EF Core versions. MySQL runs with the official provider on both, and with Pomelo only on net9.0, because it doesn't support EF Core 10 yet

## Third-Party Code

- `Internal/XactJobSerializer.cs` - Adapted from Hangfire (LGPL-3.0)
- `Internal/ExpressionUtil/` - Copied from ASP.NET MVC (Apache 2.0)
- `XactJobs.UI/wwwroot/lib/` - Bootstrap, Bootstrap Icons, Chart.js, htmx (MIT, license files next to them)
