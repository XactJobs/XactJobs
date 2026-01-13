-- ============================
-- Table storing scheduled jobs
-- ============================

ALTER TABLE "xact_jobs_job" ADD COLUMN "periodic_job_version" INTEGER;

-- ===============================
-- Table storing execution history
-- ===============================

ALTER TABLE "xact_jobs_job_history" ADD COLUMN "periodic_job_version" INTEGER;

-- ============================
-- Table storing recurring jobs
-- ============================

ALTER TABLE "xact_jobs_job_periodic" ADD COLUMN "version" INTEGER NOT NULL DEFAULT 1;
