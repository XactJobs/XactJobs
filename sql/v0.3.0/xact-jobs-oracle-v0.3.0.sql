-- ============================
-- Table storing scheduled jobs
-- ============================

-- The time a job was originally scheduled for, kept by its retries (null: the same as SCHEDULED_AT)
ALTER TABLE "XACT_JOBS_JOB" ADD ("ORIGINALLY_SCHEDULED_AT" TIMESTAMP);

-- To find the runs of a periodic job (e.g. when it's paused, or ensured at start), without reading the whole table.
-- Oracle doesn't store the rows where all the index's columns are null, so it holds only the rows of periodic jobs: one pending
-- run each, and the other jobs' inserts and deletes don't write to it.
CREATE INDEX "IX_JOB_PERIODIC_JOB_ID" ON "XACT_JOBS_JOB" ("PERIODIC_JOB_ID");

-- ===============================
-- Table storing execution history
-- ===============================

ALTER TABLE "XACT_JOBS_JOB_HISTORY" ADD ("ORIGINALLY_SCHEDULED_AT" TIMESTAMP);
