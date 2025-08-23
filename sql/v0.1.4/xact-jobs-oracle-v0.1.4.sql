-- ============================
-- Table storing scheduled jobs
-- ============================

ALTER TABLE "XACT_JOBS_JOB" ADD ("PERIODIC_JOB_VERSION" NUMBER(10));

-- ===============================
-- Table storing execution history
-- ===============================

ALTER TABLE "XACT_JOBS_JOB_HISTORY" ADD ("PERIODIC_JOB_VERSION" NUMBER(10));

-- ============================
-- Table storing recurring jobs
-- ============================

ALTER TABLE "XACT_JOBS_JOB_PERIODIC" ADD ("VERSION" NUMBER(10) DEFAULT 1 NOT NULL);

