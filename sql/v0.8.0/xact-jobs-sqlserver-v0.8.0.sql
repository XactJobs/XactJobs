-- ============================
-- Table storing recurring jobs
-- ============================

-- The time zone of the cron expression: an IANA id (e.g. Europe/Zagreb), or null for UTC.
ALTER TABLE [XactJobs].[JobPeriodic] ADD
  [TimeZone] nvarchar(100) NULL;
