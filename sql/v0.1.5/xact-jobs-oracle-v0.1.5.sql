-- ============================
-- Table storing scheduled jobs
-- ============================

-- needed to change from nvarchar(max) so it can be a part of an index below
ALTER TABLE "XACT_JOBS_JOB" MODIFY "PERIODIC_JOB_ID" NVARCHAR2(450);

CREATE UNIQUE INDEX "UQ_JOB_PERIODIC_JOB_ID" ON "XACT_JOBS_JOB" ("PERIODIC_JOB_ID", "PERIODIC_JOB_VERSION");

ALTER TABLE "XACT_JOBS_JOB" ADD CONSTRAINT "CHK_JOB_PERIODIC_JOB_ID" CHECK (
  (PERIODIC_JOB_ID IS NULL AND PERIODIC_JOB_VERSION IS NULL)
    OR (PERIODIC_JOB_ID IS NOT NULL AND PERIODIC_JOB_VERSION IS NOT NULL)
);

-- ===============================
-- Table storing execution history
-- ===============================

-- ============================
-- Table storing recurring jobs
-- ============================

