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
  [PeriodicJobId] nvarchar(max) NULL,
  [CronExpression] nvarchar(max) NULL,
  [ErrorCount] int NOT NULL,
  CONSTRAINT [PK_Job] PRIMARY KEY ([Queue], [ScheduledAt], [Id])
);

CREATE INDEX [IX_Job_Leaser] ON [XactJobs].[Job] ([Leaser]);

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
  CONSTRAINT [PK_JobHistory] PRIMARY KEY ([Id], [ProcessedAt])
);

CREATE INDEX [IX_JobHistory_ProcessedAt] ON [XactJobs].[JobHistory] ([ProcessedAt]);

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
  CONSTRAINT [PK_JobPeriodic] PRIMARY KEY ([Id])
);
