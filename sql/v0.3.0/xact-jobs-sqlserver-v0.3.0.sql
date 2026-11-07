-- ============================
-- Table storing scheduled jobs
-- ============================

-- The time a job was originally scheduled for, kept by its retries (null: the same as ScheduledAt)
ALTER TABLE [XactJobs].[Job] ADD
  [OriginallyScheduledAt] datetime2 NULL;

-- To find the runs of a periodic job (e.g. when it's paused, or ensured at start), without reading the whole table.
-- Only the rows of periodic jobs: one pending run each, so it stays small, and the other jobs' inserts and deletes don't write to it.
-- A filtered index needs QUOTED_IDENTIFIER and ANSI_NULLS on, to create it and to write to the table: SqlClient's defaults
-- (sqlcmd needs -I).
-- nvarchar(max) can't be indexed: the column gets the type of the periodic job's id, so every value fits.
ALTER TABLE [XactJobs].[Job] ALTER COLUMN [PeriodicJobId] nvarchar(450) NULL;

CREATE INDEX [IX_Job_PeriodicJobId] ON [XactJobs].[Job] ([PeriodicJobId]) WHERE [PeriodicJobId] IS NOT NULL;

-- ===============================
-- Table storing execution history
-- ===============================

ALTER TABLE [XactJobs].[JobHistory] ADD
  [OriginallyScheduledAt] datetime2 NULL;
