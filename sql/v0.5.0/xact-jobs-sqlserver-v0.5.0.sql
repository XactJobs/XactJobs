-- ============================
-- Table storing scheduled jobs
-- ============================

-- The W3C traceparent of the activity that enqueued the job, if any: its attempts' activities link to that trace. Retries keep it.
ALTER TABLE [XactJobs].[Job] ADD
  [TraceParent] nvarchar(max) NULL;

-- ===============================
-- Table storing execution history
-- ===============================

ALTER TABLE [XactJobs].[JobHistory] ADD
  [TraceParent] nvarchar(max) NULL;
