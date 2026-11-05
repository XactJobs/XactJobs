-- ============================
-- Table storing scheduled jobs
-- ============================

-- Batches and continuations
ALTER TABLE [XactJobs].[Job] ADD
  [BatchId] uniqueidentifier NULL,
  [AwaitsBatchId] uniqueidentifier NULL,
  [ContinuationCondition] int NULL;

CREATE INDEX [IX_Job_BatchId] ON [XactJobs].[Job] ([BatchId]);
CREATE INDEX [IX_Job_AwaitsBatchId] ON [XactJobs].[Job] ([AwaitsBatchId]);

-- ===============================
-- Table storing execution history
-- ===============================

ALTER TABLE [XactJobs].[JobHistory] ADD
  [BatchId] uniqueidentifier NULL,
  [AwaitsBatchId] uniqueidentifier NULL,
  [ContinuationCondition] int NULL;

CREATE INDEX [IX_JobHistory_BatchId] ON [XactJobs].[JobHistory] ([BatchId]);
CREATE INDEX [IX_JobHistory_AwaitsBatchId] ON [XactJobs].[JobHistory] ([AwaitsBatchId]);
