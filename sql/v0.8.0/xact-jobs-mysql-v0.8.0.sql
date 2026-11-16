-- ============================
-- Table storing recurring jobs
-- ============================

-- The time zone of the cron expression: an IANA id (e.g. Europe/Zagreb), or null for UTC.
ALTER TABLE `xact_jobs_job_periodic`
  ADD COLUMN `time_zone` varchar(100) CHARACTER SET utf8mb4 NULL;
