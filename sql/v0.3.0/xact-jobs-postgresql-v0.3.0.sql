-- ============================
-- Table storing scheduled jobs
-- ============================

-- The time a job was originally scheduled for, kept by its retries (null: the same as scheduled_at)
ALTER TABLE xact_jobs.job
  ADD COLUMN originally_scheduled_at timestamptz;

-- ===============================
-- Table storing execution history
-- ===============================

ALTER TABLE xact_jobs.job_history
  ADD COLUMN originally_scheduled_at timestamptz;
