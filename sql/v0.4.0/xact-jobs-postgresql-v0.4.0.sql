-- ============================
-- Table storing scheduled jobs
-- ============================

-- The batch columns' indexes leave out the rows where the column is null: most jobs are in no batch, and their inserts and deletes
-- no longer write to these indexes. The queries that use them look up a batch by its id, which implies that the column isn't null.
DROP INDEX xact_jobs.ix_job_batch_id;
CREATE INDEX ix_job_batch_id ON xact_jobs.job (batch_id) WHERE batch_id IS NOT NULL;

DROP INDEX xact_jobs.ix_job_awaits_batch_id;
CREATE INDEX ix_job_awaits_batch_id ON xact_jobs.job (awaits_batch_id) WHERE awaits_batch_id IS NOT NULL;

-- ===============================
-- Table storing execution history
-- ===============================

DROP INDEX xact_jobs.ix_job_history_batch_id;
CREATE INDEX ix_job_history_batch_id ON xact_jobs.job_history (batch_id) WHERE batch_id IS NOT NULL;

DROP INDEX xact_jobs.ix_job_history_awaits_batch_id;
CREATE INDEX ix_job_history_awaits_batch_id ON xact_jobs.job_history (awaits_batch_id) WHERE awaits_batch_id IS NOT NULL;
