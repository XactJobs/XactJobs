# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build Commands

```bash
# Build solution
dotnet build src/XactJobs.sln

# Build specific framework
dotnet build -f net8.0 src/XactJobs.sln
dotnet build -f net9.0 src/XactJobs.sln

# Release build
dotnet build -c Release src/XactJobs.sln

# Pack NuGet package
dotnet pack -c Release src/XactJobs/XactJobs.csproj
```

## Test Commands

```bash
# Run all tests
dotnet test src/XactJobs.sln

# Run single test class
dotnet test --filter "FullyQualifiedName~SqliteDatabaseIntegrationTests" src/XactJobs.Tests/XactJobs.Tests.csproj

# Run with verbose output
dotnet test --verbosity=detailed src/XactJobs.Tests/XactJobs.Tests.csproj

# Run with code coverage
dotnet test --collect:"XPlat Code Coverage" src/XactJobs.Tests/XactJobs.Tests.csproj
```

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

- net8.0, net9.0 (library)
- net10.0 (tests only)

## EF Migrations

```bash
# Restore tools first
dotnet tool restore

# Create migration (from TestWorker project)
dotnet ef migrations add MigrationName -p src/XactJobs.TestWorker/
```

## Third-Party Code

- `Internal/XactJobSerializer.cs` - Adapted from Hangfire (LGPL-3.0)
- `Internal/ExpressionUtil/` - Copied from ASP.NET MVC (Apache 2.0)
