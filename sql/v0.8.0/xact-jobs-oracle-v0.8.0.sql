-- ============================
-- Table storing recurring jobs
-- ============================

-- The time zone of the cron expression: an IANA id (e.g. Europe/Zagreb), or null for UTC.
ALTER TABLE "XACT_JOBS_JOB_PERIODIC" ADD ("TIME_ZONE" NVARCHAR2(100));
