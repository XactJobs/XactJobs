-- ============================
-- Table storing recurring jobs
-- ============================

-- The time zone of the cron expression: an IANA id (e.g. Europe/Zagreb), or null for UTC.
ALTER TABLE xact_jobs.job_periodic
  ADD COLUMN time_zone character varying(100);
