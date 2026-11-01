-- ============================
-- Table storing scheduled jobs
-- ============================

-- Jobs are now claimed and leased by primary key, so this index is no longer needed.
-- Run this after upgrading all workers: older versions need the index.
DROP INDEX IF EXISTS [IX_Job_Leaser] ON [XactJobs].[Job];
