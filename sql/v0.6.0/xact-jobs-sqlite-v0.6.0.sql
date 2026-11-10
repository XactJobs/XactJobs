-- ===============================
-- Table storing execution history
-- ===============================

-- A periodic run's state: the one it started with, and the one after it (the same, unless it completed and set one).
ALTER TABLE "xact_jobs_job_history" ADD COLUMN "previous_state" TEXT NULL;
ALTER TABLE "xact_jobs_job_history" ADD COLUMN "state" TEXT NULL;

-- ============================
-- Table storing recurring jobs
-- ============================

-- The state the last completed run left for the next one (XactJobContext.PeriodicState), at most 2000 characters.
ALTER TABLE "xact_jobs_job_periodic" ADD COLUMN "state" TEXT NULL;
