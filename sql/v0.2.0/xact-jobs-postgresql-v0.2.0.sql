-- ============================
-- Table storing scheduled jobs
-- ============================

-- Batches and continuations. Claiming a job still updates only non-indexed columns, so it stays a HOT update.
ALTER TABLE xact_jobs.job
  ADD COLUMN batch_id uuid,
  ADD COLUMN awaits_batch_id uuid,
  ADD COLUMN continuation_condition integer;

CREATE INDEX ix_job_batch_id ON xact_jobs.job (batch_id);
CREATE INDEX ix_job_awaits_batch_id ON xact_jobs.job (awaits_batch_id);

-- ===============================
-- Table storing execution history
-- ===============================

ALTER TABLE xact_jobs.job_history
  ADD COLUMN batch_id uuid,
  ADD COLUMN awaits_batch_id uuid,
  ADD COLUMN continuation_condition integer;

CREATE INDEX ix_job_history_batch_id ON xact_jobs.job_history (batch_id);
CREATE INDEX ix_job_history_awaits_batch_id ON xact_jobs.job_history (awaits_batch_id);
