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

## XactJobs.UI - Dashboard Project

XactJobs.UI is a Razor Pages library providing a web-based dashboard and management interface for monitoring and controlling XactJobs background processing.

### UI Project Structure

```
src/XactJobs.UI/
├── Api/
│   └── XactJobsApiEndpoints.cs         # REST API endpoints (10 endpoints)
├── Areas/XactJobs/Pages/
│   ├── Index.cshtml(.cs)               # Dashboard with stats & charts
│   ├── Scheduled/Index.cshtml(.cs)     # Pending jobs management
│   ├── Succeeded/Index.cshtml(.cs)     # Completed job history
│   ├── Failed/Index.cshtml(.cs)        # Failed jobs with retry
│   ├── Periodic/Index.cshtml(.cs)      # Recurring job control
│   ├── Partials/_Pagination.cshtml     # Shared pagination component
│   └── _Layout.cshtml                  # Master layout template
├── DependencyInjection/
│   ├── ServiceCollectionExtensions.cs  # AddXactJobsUI<TDbContext>()
│   └── ApplicationBuilderExtensions.cs # MapXactJobsUI()
├── Services/
│   ├── IXactJobsUIService.cs           # Service interface (15 methods)
│   ├── XactJobsUIService.cs            # EF Core implementation
│   └── Models/                         # DTOs for API/UI
├── wwwroot/
│   ├── css/xactjobs.css                # Custom styles
│   └── js/xactjobs.js                  # HTMX handlers & utilities
└── XactJobsUIOptionsBuilder.cs         # Fluent configuration
```

### UI Features

- **Dashboard**: Real-time stats (pending, processing, succeeded/failed 24h), Chart.js visualizations
- **Scheduled Jobs**: View, requeue, or delete pending jobs with queue filtering
- **Job History**: Browse succeeded/failed jobs with error details and stack traces
- **Periodic Jobs**: Enable/disable, trigger on-demand, view cron expressions
- **Multi-Queue**: Filter jobs by queue name across all views

### UI Technology Stack

**Server-Side:**
- **SDK**: `Microsoft.NET.Sdk.Razor` with `AddRazorSupportForMvc=true`
- **Framework**: ASP.NET Core Razor Pages in `/Areas/XactJobs/` namespace
- **API**: ASP.NET Core Minimal APIs with endpoint groups and route constraints
- **Data Access**: EF Core via generic `XactJobsUIService<TDbContext>` querying XactJob entities
- **DI Pattern**: Generic service registration `AddXactJobsUI<TDbContext>()`

**Client-Side Libraries (CDN):**
- **Bootstrap 5.3.3** - Responsive UI framework with dark navbar, cards, tables, modals, badges
- **Bootstrap Icons 1.11.3** - Icon font (bi-* classes throughout UI)
- **Chart.js 4.4.1** - Line charts for job trends, doughnut chart for success rates
- **HTMX 1.9.10** - Declarative AJAX via `hx-post`, `hx-delete`, `hx-confirm`, `hx-swap` attributes

**Custom Assets (`wwwroot/`):**
- `css/xactjobs.css` - CSS custom properties, gradient stat cards, HTMX animations, scrollbar styling
- `js/xactjobs.js` - HTMX event handlers (loading spinners, error handling), utility formatters

**UI Patterns:**
- HTMX for button actions (requeue, delete, retry, toggle) without page reload
- Bootstrap modals for error details with stack traces
- Auto-refresh dashboard stats via JavaScript fetch at configurable intervals
- Shared pagination partial (`_Pagination.cshtml`) across all list views
- Status badges: `bg-primary` (pending), `bg-warning` (processing), `bg-success` (completed), `bg-danger` (failed)

**Static Asset Serving:**
- Assets served via `~/_content/XactJobs.UI/` (standard Razor Class Library pattern)
- Embedded in NuGet package for seamless host application integration

### UI Registration

```csharp
// In Program.cs or Startup.cs
services.AddXactJobsUI<AppDbContext>(options =>
{
    options.BasePath = "/jobs";              // Default: "/XactJobs"
    options.ApiBasePath = "/api/jobs";       // Default: "/api/XactJobs"
    options.DefaultPageSize = 25;            // Default: 20
    options.RefreshIntervalSeconds = 15;     // Default: 30 (range: 5-300)
    options.DashboardTitle = "Job Monitor";  // Default: "XactJobs Dashboard"
    options.RequireAuthorizationPolicy("AdminOnly"); // Optional auth policy
});

// Map endpoints
app.MapXactJobsUI();
```

### UI API Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/XactJobs/stats` | GET | Dashboard statistics |
| `/api/XactJobs/chart-data` | GET | Time-series chart data |
| `/api/XactJobs/jobs` | GET | List scheduled jobs |
| `/api/XactJobs/jobs/{id}` | GET/DELETE | Get or delete job |
| `/api/XactJobs/jobs/{id}/requeue` | POST | Requeue a job |
| `/api/XactJobs/history/succeeded` | GET | List succeeded jobs |
| `/api/XactJobs/history/failed` | GET | List failed jobs |
| `/api/XactJobs/history/{id}/retry` | POST | Retry failed job |
| `/api/XactJobs/periodic` | GET | List periodic jobs |
| `/api/XactJobs/periodic/{id}/toggle` | POST | Toggle active state |
| `/api/XactJobs/periodic/{id}/trigger` | POST | Trigger immediate run |

### UI Service Architecture

**Interface** (`IXactJobsUIService` - 15 methods):
```csharp
// Dashboard (2 methods)
Task<DashboardStatsDto> GetDashboardStatsAsync(CancellationToken ct);
Task<IReadOnlyList<JobCountsOverTimeDto>> GetJobCountsOverTimeAsync(DateTime from, DateTime to, TimeSpan interval, CancellationToken ct);

// Scheduled Jobs (4 methods)
Task<PagedResultDto<JobDto>> GetScheduledJobsAsync(int page, int pageSize, string? queue, CancellationToken ct);
Task<JobDto?> GetJobByIdAsync(long id, CancellationToken ct);
Task<bool> DeleteJobAsync(long id, CancellationToken ct);
Task<bool> RequeueJobAsync(long id, CancellationToken ct);

// Job History (4 methods)
Task<PagedResultDto<JobHistoryDto>> GetSucceededJobsAsync(int page, int pageSize, string? queue, CancellationToken ct);
Task<PagedResultDto<JobHistoryDto>> GetFailedJobsAsync(int page, int pageSize, string? queue, CancellationToken ct);
Task<JobHistoryDto?> GetJobHistoryByIdAsync(long id, CancellationToken ct);
Task<bool> RetryFailedJobAsync(long id, CancellationToken ct);

// Periodic Jobs (5 methods)
Task<PagedResultDto<PeriodicJobDto>> GetPeriodicJobsAsync(int page, int pageSize, CancellationToken ct);
Task<PeriodicJobDto?> GetPeriodicJobByIdAsync(string id, CancellationToken ct);
Task<bool> TogglePeriodicJobAsync(string id, bool isActive, CancellationToken ct);
Task<bool> TriggerPeriodicJobNowAsync(string id, CancellationToken ct);
Task<bool> DeletePeriodicJobAsync(string id, CancellationToken ct);
```

**DTOs** (`Services/Models/`):
- `DashboardStatsDto` - Pending, processing, succeeded/failed 24h counts, periodic active/inactive, success rate
- `JobCountsOverTimeDto` - Time-series data points for Chart.js (timestamp, succeeded, failed counts)
- `JobDto` - Scheduled job with computed `IsProcessing` (based on `LeasedUntil`) and `DisplayTypeName`
- `JobHistoryDto` - Completed job with duration calculation, error message, stack trace
- `PeriodicJobDto` - Recurring job with cron expression, active state, version, next run time
- `PagedResultDto<T>` - Generic wrapper with `TotalPages`, `HasPreviousPage`, `HasNextPage`

**Implementation** (`XactJobsUIService<TDbContext>`):
- Generic over any `DbContext` that has XactJobs entities configured
- Queries `DbContext.Set<XactJob>()`, `DbContext.Set<XactJobHistory>()`, `DbContext.Set<XactJobPeriodic>()`
- Requeue: Resets `ScheduledAt` to now, clears `LeasedUntil`, resets `ErrorCount` to 0
- Retry: Creates new `XactJob` from failed `XactJobHistory` record

## Target Frameworks

- net8.0, net9.0 (library and UI)
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
