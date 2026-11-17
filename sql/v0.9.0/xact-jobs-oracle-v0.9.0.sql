-- ============================
-- Table storing recurring jobs
-- ============================

-- The local times a periodic job doesn't run at (dates and daily time ranges, see XactCronSchedule.Exclusions), or null.
-- Not part of its definition: a change doesn't make a new version. At most 2000 characters.
ALTER TABLE "XACT_JOBS_JOB_PERIODIC" ADD ("EXCLUSIONS" NVARCHAR2(2000));
