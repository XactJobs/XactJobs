-- ============================
-- Table storing scheduled jobs
-- ============================

ALTER TABLE `xact_jobs_job` MODIFY COLUMN `periodic_job_id` varchar(255) CHARACTER SET utf8mb4 NULL;

CREATE UNIQUE INDEX `uq_job_periodic_job_id` ON `xact_jobs_job` (`periodic_job_id`, `periodic_job_version`);

ALTER TABLE `xact_jobs_job` ADD CONSTRAINT `chk_job_periodic_job_id` CHECK (
  (periodic_job_id IS NULL AND periodic_job_version IS NULL)
    OR (periodic_job_id IS NOT NULL AND periodic_job_version IS NOT NULL)
);

-- ===============================
-- Table storing execution history
-- ===============================

-- ============================
-- Table storing recurring jobs
-- ============================
