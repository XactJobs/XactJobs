-- ============================
-- Table storing scheduled jobs
-- ============================

-- One pending run per periodic job and version. Runs from before v0.1.4 have no version, which counts as 1: they get it, so that
-- the index treats them like the others. If a periodic job already has two runs of one version, creating the index fails: run the
-- script so that it stops at the first error, find them with:
--   SELECT periodic_job_id, periodic_job_version, COUNT(*) FROM xact_jobs.job WHERE periodic_job_id IS NOT NULL
--     GROUP BY periodic_job_id, periodic_job_version HAVING COUNT(*) > 1;
-- delete the extra runs, and run the script again.

UPDATE xact_jobs.job SET periodic_job_version = 1 WHERE periodic_job_id IS NOT NULL AND periodic_job_version IS NULL;

CREATE UNIQUE INDEX ix_job_periodic_job_id_version ON xact_jobs.job (periodic_job_id, periodic_job_version) WHERE periodic_job_id IS NOT NULL;

-- the new index starts with the same column
DROP INDEX xact_jobs.ix_job_periodic_job_id;

-- ===============================
-- Table storing execution history
-- ===============================

-- A periodic run's state: the one it started with, and the one after it (the same, unless it completed and set one).
ALTER TABLE xact_jobs.job_history
  ADD COLUMN previous_state character varying(2000),
  ADD COLUMN state character varying(2000);

-- ============================
-- Table storing recurring jobs
-- ============================

-- The state the last completed run left for the next one (XactJobContext.PeriodicState), at most 2000 characters.
ALTER TABLE xact_jobs.job_periodic
  ADD COLUMN state character varying(2000);
