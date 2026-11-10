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
