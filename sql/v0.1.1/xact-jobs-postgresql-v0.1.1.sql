-- ============================
-- Table storing scheduled jobs
-- ============================

ALTER TABLE xact_jobs.job ADD COLUMN periodic_job_version INT;

-- ===============================
-- Table storing execution history
-- ===============================

ALTER TABLE xact_jobs.job_history ADD COLUMN periodic_job_version INT;

-- ============================
-- Table storing recurring jobs
-- ============================

ALTER TABLE xact_jobs.job_periodic ADD COLUMN version INT NOT NULL DEFAULT 1;
