-- ============================
-- Table storing scheduled jobs
-- ============================

-- One pending run per periodic job and version. Runs from before v0.1.4 have no version, which counts as 1: they get it, so that
-- the index treats them like the others. If a periodic job already has two runs of one version, creating the index fails: run the
-- script so that it stops at the first error, find them with:
--   SELECT "PERIODIC_JOB_ID", "PERIODIC_JOB_VERSION", COUNT(*) FROM "XACT_JOBS_JOB" WHERE "PERIODIC_JOB_ID" IS NOT NULL
--     GROUP BY "PERIODIC_JOB_ID", "PERIODIC_JOB_VERSION" HAVING COUNT(*) > 1;
-- delete the extra runs, and run the script again.

UPDATE "XACT_JOBS_JOB" SET "PERIODIC_JOB_VERSION" = 1 WHERE "PERIODIC_JOB_ID" IS NOT NULL AND "PERIODIC_JOB_VERSION" IS NULL;
COMMIT;

-- a plain index: Oracle leaves out the rows where all its columns are null, so it holds only the rows of periodic jobs
CREATE UNIQUE INDEX "IX_JOB_PERIODIC_JOB_ID_VERSION" ON "XACT_JOBS_JOB" ("PERIODIC_JOB_ID", "PERIODIC_JOB_VERSION");

-- the new index starts with the same column
DROP INDEX "IX_JOB_PERIODIC_JOB_ID";

-- ===============================
-- Table storing execution history
-- ===============================

-- A periodic run's state: the one it started with, and the one after it (the same, unless it completed and set one).
ALTER TABLE "XACT_JOBS_JOB_HISTORY" ADD ("PREVIOUS_STATE" NVARCHAR2(2000), "STATE" NVARCHAR2(2000));

-- ============================
-- Table storing recurring jobs
-- ============================

-- The state the last completed run left for the next one (XactJobContext.PeriodicState), at most 2000 characters.
ALTER TABLE "XACT_JOBS_JOB_PERIODIC" ADD ("STATE" NVARCHAR2(2000));
