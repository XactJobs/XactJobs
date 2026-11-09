-- ============================
-- Table storing scheduled jobs
-- ============================

-- The W3C traceparent of the activity that enqueued the job, if any: its attempts' activities link to that trace. Retries keep it.
ALTER TABLE "XACT_JOBS_JOB" ADD ("TRACE_PARENT" NVARCHAR2(2000));

-- ===============================
-- Table storing execution history
-- ===============================

ALTER TABLE "XACT_JOBS_JOB_HISTORY" ADD ("TRACE_PARENT" NVARCHAR2(2000));
