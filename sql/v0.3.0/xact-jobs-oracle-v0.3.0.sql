-- ============================
-- Table storing scheduled jobs
-- ============================

-- The time a job was originally scheduled for, kept by its retries (null: the same as SCHEDULED_AT)
ALTER TABLE "XACT_JOBS_JOB" ADD ("ORIGINALLY_SCHEDULED_AT" TIMESTAMP);

-- ===============================
-- Table storing execution history
-- ===============================

ALTER TABLE "XACT_JOBS_JOB_HISTORY" ADD ("ORIGINALLY_SCHEDULED_AT" TIMESTAMP);
