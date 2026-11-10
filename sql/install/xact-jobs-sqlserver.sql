-- XactJobs v0.6.0, SQL Server: the whole schema, for a new installation.
-- It's the same as running the versioned scripts (sql/v*/) in order. To upgrade an existing installation, run only the versioned
-- scripts newer than its version.
-- The filtered index needs QUOTED_IDENTIFIER and ANSI_NULLS on, to create it and to write to the table: SqlClient's defaults
-- (sqlcmd needs -I).

IF SCHEMA_ID(N'XactJobs') IS NULL EXEC(N'CREATE SCHEMA [XactJobs];');

-- ============================
-- Table storing scheduled jobs
-- ============================

CREATE TABLE [XactJobs].[Job] (
  [Id] bigint NOT NULL IDENTITY,
  [ScheduledAt] datetime2 NOT NULL,
  [Queue] nvarchar(50) NOT NULL,
  [LeasedUntil] datetime2 NULL,
  [Leaser] uniqueidentifier NULL,
  [TypeName] nvarchar(max) NOT NULL,
  [MethodName] nvarchar(max) NOT NULL,
  [MethodArgs] nvarchar(max) NOT NULL,
  [PeriodicJobId] nvarchar(450) NULL,
  [CronExpression] nvarchar(max) NULL,
  [ErrorCount] int NOT NULL,
  [PeriodicJobVersion] int NULL,
  [BatchId] uniqueidentifier NULL,
  [AwaitsBatchId] uniqueidentifier NULL,
  [ContinuationCondition] int NULL,
  -- the time a job was originally scheduled for, kept by its retries (null: the same as ScheduledAt)
  [OriginallyScheduledAt] datetime2 NULL,
  -- the W3C traceparent of the activity that enqueued the job, if any: its attempts' activities link to that trace
  [TraceParent] nvarchar(max) NULL,
  CONSTRAINT [PK_Job] PRIMARY KEY ([Queue], [ScheduledAt], [Id])
);

-- batches and continuations: only the rows of a batch, or of a continuation (most jobs are in none)
CREATE INDEX [IX_Job_BatchId] ON [XactJobs].[Job] ([BatchId]) WHERE [BatchId] IS NOT NULL;
CREATE INDEX [IX_Job_AwaitsBatchId] ON [XactJobs].[Job] ([AwaitsBatchId]) WHERE [AwaitsBatchId] IS NOT NULL;

-- the runs of a periodic job: only the rows of periodic jobs, so the other jobs' inserts and deletes don't write to it
CREATE INDEX [IX_Job_PeriodicJobId] ON [XactJobs].[Job] ([PeriodicJobId]) WHERE [PeriodicJobId] IS NOT NULL;

-- ===============================
-- Table storing execution history
-- ===============================

CREATE TABLE [XactJobs].[JobHistory] (
  [Id] bigint NOT NULL,
  [ProcessedAt] datetime2 NOT NULL,
  [Status] int NOT NULL,
  [ErrorMessage] nvarchar(max) NULL,
  [ErrorStackTrace] nvarchar(max) NULL,
  [ScheduledAt] datetime2 NOT NULL,
  [TypeName] nvarchar(max) NOT NULL,
  [MethodName] nvarchar(max) NOT NULL,
  [MethodArgs] nvarchar(max) NOT NULL,
  [Queue] nvarchar(max) NOT NULL,
  [PeriodicJobId] nvarchar(max) NULL,
  [CronExpression] nvarchar(max) NULL,
  [ErrorCount] int NOT NULL,
  [PeriodicJobVersion] int NULL,
  [BatchId] uniqueidentifier NULL,
  [AwaitsBatchId] uniqueidentifier NULL,
  [ContinuationCondition] int NULL,
  [OriginallyScheduledAt] datetime2 NULL,
  -- the W3C traceparent of the activity that enqueued the job, if any: its attempts' activities link to that trace
  [TraceParent] nvarchar(max) NULL,
  -- a periodic run's state: the one it started with, and the one after it (the same, unless it completed and set one)
  [PreviousState] nvarchar(2000) NULL,
  [State] nvarchar(2000) NULL,
  CONSTRAINT [PK_JobHistory] PRIMARY KEY ([Id], [ProcessedAt])
);

CREATE INDEX [IX_JobHistory_ProcessedAt] ON [XactJobs].[JobHistory] ([ProcessedAt]);
CREATE INDEX [IX_JobHistory_BatchId] ON [XactJobs].[JobHistory] ([BatchId]) WHERE [BatchId] IS NOT NULL;
CREATE INDEX [IX_JobHistory_AwaitsBatchId] ON [XactJobs].[JobHistory] ([AwaitsBatchId]) WHERE [AwaitsBatchId] IS NOT NULL;

-- ============================
-- Table storing recurring jobs
-- ============================

CREATE TABLE [XactJobs].[JobPeriodic] (
  [Id] nvarchar(450) NOT NULL,
  [CreatedAt] datetime2 NOT NULL,
  [UpdatedAt] datetime2 NOT NULL,
  [CronExpression] nvarchar(max) NOT NULL,
  [TypeName] nvarchar(max) NOT NULL,
  [MethodName] nvarchar(max) NOT NULL,
  [MethodArgs] nvarchar(max) NOT NULL,
  [Queue] nvarchar(max) NOT NULL,
  [IsActive] bit NOT NULL,
  [Version] int NOT NULL CONSTRAINT [DF_XactJobs_JobPeriodic_Version] DEFAULT 1,
  -- the state the last completed run left for the next one (XactJobContext.PeriodicState), at most 2000 characters
  [State] nvarchar(2000) NULL,
  CONSTRAINT [PK_JobPeriodic] PRIMARY KEY ([Id])
);
