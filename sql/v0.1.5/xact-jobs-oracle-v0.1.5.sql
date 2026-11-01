-- ============================
-- Table storing scheduled jobs
-- ============================

-- Jobs are now claimed and leased by primary key, so this index is no longer needed.
-- Run this after upgrading all workers: older versions need the index.
DROP INDEX "IX_JOB_LEASER";

-- ===============================
-- Table storing execution history
-- ===============================

-- A failure that doesn't fit in NVARCHAR2(2000) can't be recorded, so the job would keep retrying.
-- Oracle can't change these columns to NCLOB directly, so they are replaced.
ALTER TABLE "XACT_JOBS_JOB_HISTORY" ADD ("ERROR_MESSAGE_NEW" NCLOB, "ERROR_STACK_TRACE_NEW" NCLOB);
UPDATE "XACT_JOBS_JOB_HISTORY" SET "ERROR_MESSAGE_NEW" = "ERROR_MESSAGE", "ERROR_STACK_TRACE_NEW" = "ERROR_STACK_TRACE";
ALTER TABLE "XACT_JOBS_JOB_HISTORY" DROP ("ERROR_MESSAGE", "ERROR_STACK_TRACE");
ALTER TABLE "XACT_JOBS_JOB_HISTORY" RENAME COLUMN "ERROR_MESSAGE_NEW" TO "ERROR_MESSAGE";
ALTER TABLE "XACT_JOBS_JOB_HISTORY" RENAME COLUMN "ERROR_STACK_TRACE_NEW" TO "ERROR_STACK_TRACE";
