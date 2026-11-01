-- ============================
-- Table storing scheduled jobs
-- ============================

-- Lease statements now find jobs by primary key, so this index is no longer needed.
-- Without it, claiming a job updates only non-indexed columns, which allows HOT updates.
DROP INDEX IF EXISTS xact_jobs.ix_job_leaser;

-- Leaves free space on each page, so that updated rows can stay on the same page (required for HOT updates).
-- This affects new pages only: existing pages are not rewritten.
ALTER TABLE xact_jobs.job SET (fillfactor = 70);
