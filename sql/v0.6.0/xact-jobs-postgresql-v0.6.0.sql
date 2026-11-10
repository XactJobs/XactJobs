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
