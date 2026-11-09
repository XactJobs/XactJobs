-- ============================
-- Table storing scheduled jobs
-- ============================

-- The W3C traceparent of the activity that enqueued the job, if any: its attempts' activities link to that trace. Retries keep it.
ALTER TABLE "xact_jobs_job" ADD COLUMN "trace_parent" TEXT NULL;

-- ===============================
-- Table storing execution history
-- ===============================

ALTER TABLE "xact_jobs_job_history" ADD COLUMN "trace_parent" TEXT NULL;
