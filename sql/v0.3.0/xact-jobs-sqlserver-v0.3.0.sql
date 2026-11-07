-- ============================
-- Table storing scheduled jobs
-- ============================

-- The time a job was originally scheduled for, kept by its retries (null: the same as ScheduledAt)
ALTER TABLE [XactJobs].[Job] ADD
  [OriginallyScheduledAt] datetime2 NULL;

-- ===============================
-- Table storing execution history
-- ===============================

ALTER TABLE [XactJobs].[JobHistory] ADD
  [OriginallyScheduledAt] datetime2 NULL;
