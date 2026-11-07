-- ============================
-- Table storing scheduled jobs
-- ============================

-- The time a job was originally scheduled for, kept by its retries (null: the same as scheduled_at)
ALTER TABLE `xact_jobs_job`
  ADD COLUMN `originally_scheduled_at` datetime(6) NULL;

-- ===============================
-- Table storing execution history
-- ===============================

ALTER TABLE `xact_jobs_job_history`
  ADD COLUMN `originally_scheduled_at` datetime(6) NULL;
