-- ============================
-- Table storing scheduled jobs
-- ============================

-- The time a job was originally scheduled for, kept by its retries (null: the same as scheduled_at)
ALTER TABLE xact_jobs.job
  ADD COLUMN originally_scheduled_at timestamptz;

-- To find the runs of a periodic job (e.g. when it's paused, or ensured at start), without reading the whole table.
-- Only the rows of periodic jobs: one pending run each, so it stays small, and the other jobs' inserts and deletes don't write to it.
-- Claims don't change the column, so they stay HOT updates.
CREATE INDEX ix_job_periodic_job_id ON xact_jobs.job (periodic_job_id) WHERE periodic_job_id IS NOT NULL;

-- ===============================
-- Table storing execution history
-- ===============================

ALTER TABLE xact_jobs.job_history
  ADD COLUMN originally_scheduled_at timestamptz;
