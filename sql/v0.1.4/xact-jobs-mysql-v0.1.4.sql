-- ============================
-- Table storing scheduled jobs
-- ============================

ALTER TABLE `xact_jobs_job` ADD `periodic_job_version` INT NULL;

-- ===============================
-- Table storing execution history
-- ===============================

ALTER TABLE `xact_jobs_job_history` ADD `periodic_job_version` INT NULL;

-- ============================
-- Table storing recurring jobs
-- ============================

ALTER TABLE `xact_jobs_job_periodic` ADD `version` INT NOT NULL DEFAULT 1;
