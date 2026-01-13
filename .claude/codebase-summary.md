# XactJobs Codebase Summary

## Project Overview
XactJobs is a .NET background job processing library with integrated database transaction support using Entity Framework Core. Version 0.1.4, licensed under LGPL-3.0-or-later.

## Project Type
Library (NuGet package) for reliable job scheduling and execution with full database transaction support.

## Technology Stack

### Backend
- .NET 8.0 / 9.0
- Entity Framework Core 9.0.8
- Microsoft.Extensions.DependencyInjection
- Microsoft.Extensions.Hosting

### Supported Databases
- SQL Server (Microsoft.EntityFrameworkCore.SqlServer)
- PostgreSQL (Npgsql.EntityFrameworkCore.PostgreSQL 9.0.4)
- MySQL (Pomelo.EntityFrameworkCore.MySql 9.0.0)
- Oracle (Oracle.EntityFrameworkCore 9.23.90)
- SQLite (Microsoft.EntityFrameworkCore.Sqlite)

## Solution Structure

```
/workspace/
├── src/
│   ├── XactJobs/                 # Core library (main NuGet package)
│   ├── XactJobs.TestWorker/      # Test worker application
│   ├── XactJobs.TestWeb/         # Test web application
│   └── XactJobs.sln              # Solution file
├── sql/                          # Database schema scripts (v0.1.0, v0.1.4)
├── docker/                       # Docker compose files (mysql, oracle, postgresql)
└── .claude/                      # Claude reference files

```

## Core Library Structure (/workspace/src/XactJobs)

### Key Directories

**DependencyInjection/**
- `ServiceCollectionExtensions.cs` - Main DI setup with AddXactJobs<TDbContext>()
- `XactJobsRunnerDispatcher.cs` - Background service managing worker threads
- `XactJobsCronScheduler.cs` - Cron job scheduling
- `XactJobsCronOptionsScheduler.cs` - Periodic job scheduling from options

**Internal/**
- `XactJobRunner.cs` - Core job execution loop with polling and batch processing
- `XactJobSerializer.cs` - Lambda expression serialization/deserialization
- `XactJobCompiler.cs` - Expression compilation and execution
- `XactJobMaintenance.cs` - Cleanup and maintenance tasks
- `SqlDialects/` - Database-specific SQL implementations (factory pattern)
  - MySqlDialect, OracleDialect, PostgreSqlDialect, SqlServerDialect, SqliteDialect

**EntityConfigurations/**
- `XactJobEntityConfiguration.cs` - Main job queue entity
- `XactJobPeriodicEntityConfiguration.cs` - Recurring job configuration
- `XactJobHistoryEntityConfiguration.cs` - Historical job records
- `ModelBuilderExtensions.cs` - EF model configuration helpers

**CronUtil/**
- `CronSequenceGenerator.cs` - Generates next execution times from cron expressions
- Standard cron format support (second, minute, hour, day, month, day-of-week)

## Key Features

1. **Transaction-Safe Job Queuing** - Jobs stored in same database as business data
2. **Multiple Queue Support** - Default, priority, long-running, custom queues
3. **Cron-Based Scheduling** - Recurring jobs with standard cron expressions
4. **QuickPoll Optimization** - In-process channel-based notifications for immediate execution
5. **Retry Strategies** - Configurable exponential backoff
6. **Expression-Based Jobs** - Type-safe lambda expressions for job definitions

## Architecture Patterns

### Database-Backed Queue
- Jobs stored alongside business data for transactional consistency
- Polling-based workers (default 6 seconds interval)
- Lease-based locking to prevent concurrent execution
- Batch processing (default 100 jobs per batch)

### Multi-Queue Support
- Independent worker pools per queue
- Configurable worker counts and batch sizes
- Queue names: Default, Priority, LongRunning, custom

### Expression-Based Job Definition
- Lambda expressions compiled to method calls
- JSON serialization of method arguments
- Supports static and instance methods

### SQL Dialect Abstraction
- Factory pattern for database-specific queries
- EF Core Relational API for custom SQL execution

## Main API Entry Points

### DI Registration
```csharp
services.AddXactJobs<MyDbContext>(options => {
    options.WithPriorityQueue();
    options.WithLongRunningQueue();
    options.WithPeriodicJob("*/5 * * * * *", () => MyService.RunJob());
});
```

### Job Enqueueing (DbContextExtensions.cs)
```csharp
dbContext.JobEnqueue(() => EmailService.SendWelcome(userId));
dbContext.JobScheduleAt(DateTime.UtcNow.AddHours(1), () => SendReminder());
dbContext.JobScheduleIn(TimeSpan.FromMinutes(30), () => ProcessData());
dbContext.JobEnsurePeriodicAsync("daily-cleanup", "0 0 * * * *", () => Cleanup());
```

## Database Entities

- **XactJob** - Main job queue table
- **XactJobPeriodic** - Recurring job definitions with version tracking
- **XactJobHistory** - Historical job records (30-day retention default)

## Test Projects

- **XactJobs.TestWorker** - Worker service demonstrating background job usage
- **XactJobs.TestWeb** - ASP.NET Core web application demonstrating job enqueueing

## Configuration Files

- `.config/dotnet-tools.json` - Contains dotnet-ef 9.0.6 for migrations
- `src/XactJobs/XactJobs.csproj` - Core library project (auto-generates NuGet package)
- Test projects use `appsettings.json` for connection string configuration

## Important Notes

- Current version: 0.1.4
- Targets: net8.0, net9.0
- License: LGPL-3.0-or-later

## Design Patterns Used

- Dependency Injection (Microsoft.Extensions.DI)
- Factory Pattern (SQL dialect creation)
- Strategy Pattern (Retry strategies, cron sequence generation)
- Observer Pattern (QuickPoll channel notifications)
- Expression Trees (Lambda compilation and execution)
- Hosted Service Pattern (Background job workers)

## Code Metrics

- Main library: 57 C# source files
- Pure library design with no console/UI dependencies
- Clear separation: Core, DI, Internal layers
