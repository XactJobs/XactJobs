-- ============================
-- Table storing scheduled jobs
-- ============================

CREATE UNIQUE INDEX uq_job_periodic_job_id ON xact_jobs.job (periodic_job_id, periodic_job_version)
  WHERE periodic_job_id IS NOT NULL;

ALTER TABLE xact_jobs.job ADD CONSTRAINT chk_job_periodic_job_id CHECK (
  (periodic_job_id IS NULL AND periodic_job_version IS NULL)
    OR (periodic_job_id IS NOT NULL AND periodic_job_version IS NOT NULL)
);

-- ===============================
-- Table storing execution history
-- ===============================

-- ============================
-- Table storing recurring jobs
-- ============================

