-- ============================
-- Table storing scheduled jobs
-- ============================

-- The batch columns' indexes leave out the rows where the column is null: most jobs are in no batch, and their inserts and deletes
-- no longer write to these indexes. The queries that use them look up a batch by its id, which implies that the column isn't null.
-- A filtered index needs QUOTED_IDENTIFIER and ANSI_NULLS on, to create it and to write to the table: SqlClient's defaults
-- (sqlcmd needs -I).
DROP INDEX [IX_Job_BatchId] ON [XactJobs].[Job];
CREATE INDEX [IX_Job_BatchId] ON [XactJobs].[Job] ([BatchId]) WHERE [BatchId] IS NOT NULL;

DROP INDEX [IX_Job_AwaitsBatchId] ON [XactJobs].[Job];
CREATE INDEX [IX_Job_AwaitsBatchId] ON [XactJobs].[Job] ([AwaitsBatchId]) WHERE [AwaitsBatchId] IS NOT NULL;

-- ===============================
-- Table storing execution history
-- ===============================

DROP INDEX [IX_JobHistory_BatchId] ON [XactJobs].[JobHistory];
CREATE INDEX [IX_JobHistory_BatchId] ON [XactJobs].[JobHistory] ([BatchId]) WHERE [BatchId] IS NOT NULL;

DROP INDEX [IX_JobHistory_AwaitsBatchId] ON [XactJobs].[JobHistory];
CREATE INDEX [IX_JobHistory_AwaitsBatchId] ON [XactJobs].[JobHistory] ([AwaitsBatchId]) WHERE [AwaitsBatchId] IS NOT NULL;
