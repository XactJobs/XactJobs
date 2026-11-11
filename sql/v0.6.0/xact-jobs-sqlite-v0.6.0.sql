-- ============================
-- Table storing scheduled jobs
-- ============================

-- One pending run per periodic job and version. Runs from before v0.1.4 have no version, which counts as 1: they get it, so that
-- the index treats them like the others. If a periodic job already has two runs of one version, creating the index fails: run the
-- script so that it stops at the first error, find them with:
--   SELECT "periodic_job_id", "periodic_job_version", COUNT(*) FROM "xact_jobs_job" WHERE "periodic_job_id" IS NOT NULL
--     GROUP BY "periodic_job_id", "periodic_job_version" HAVING COUNT(*) > 1;
-- delete the extra runs, and run the script again.

UPDATE "xact_jobs_job" SET "periodic_job_version" = 1 WHERE "periodic_job_id" IS NOT NULL AND "periodic_job_version" IS NULL;

CREATE UNIQUE INDEX "ix_job_periodic_job_id_version" ON "xact_jobs_job" ("periodic_job_id", "periodic_job_version") WHERE "periodic_job_id" IS NOT NULL;

-- the new index starts with the same column
DROP INDEX "ix_job_periodic_job_id";

-- ===============================
-- Table storing execution history
-- ===============================

-- A periodic run's state: the one it started with, and the one after it (the same, unless it completed and set one).
ALTER TABLE "xact_jobs_job_history" ADD COLUMN "previous_state" TEXT NULL;
ALTER TABLE "xact_jobs_job_history" ADD COLUMN "state" TEXT NULL;

-- A job keeps its id across its retries (they move its row, instead of adding a new one), so the history has a row per attempt: the
-- primary key is the id and the time it was processed, as on the other databases. SQLite can't change a primary key, so the table is
-- copied into a new one.
CREATE TABLE "xact_jobs_job_history_new" (
  "id" INTEGER NOT NULL,
  "processed_at" DATETIME NOT NULL,
  "status" INTEGER NOT NULL,
  "error_message" TEXT NULL,
  "error_stack_trace" TEXT NULL,
  "scheduled_at" DATETIME NOT NULL,
  "type_name" TEXT NOT NULL,
  "method_name" TEXT NOT NULL,
  "method_args" TEXT NOT NULL,
  "queue" TEXT NOT NULL,
  "periodic_job_id" TEXT NULL,
  "cron_expression" TEXT NULL,
  "error_count" INTEGER NOT NULL,
  "periodic_job_version" INTEGER,
  "batch_id" TEXT NULL,
  "awaits_batch_id" TEXT NULL,
  "continuation_condition" INTEGER NULL,
  "originally_scheduled_at" DATETIME NULL,
  -- the W3C traceparent of the activity that enqueued the job, if any: its attempts' activities link to that trace
  "trace_parent" TEXT NULL,
  -- a periodic run's state: the one it started with, and the one after it (the same, unless it completed and set one)
  "previous_state" TEXT NULL,
  "state" TEXT NULL,
  -- a job keeps its id across its retries: a row per attempt
  CONSTRAINT "pk_job_history" PRIMARY KEY ("id", "processed_at")
);

INSERT INTO "xact_jobs_job_history_new" ("id", "processed_at", "status", "error_message", "error_stack_trace", "scheduled_at", "type_name", "method_name", "method_args", "queue", "periodic_job_id", "cron_expression", "error_count", "periodic_job_version", "batch_id", "awaits_batch_id", "continuation_condition", "originally_scheduled_at", "trace_parent", "previous_state", "state")
SELECT "id", "processed_at", "status", "error_message", "error_stack_trace", "scheduled_at", "type_name", "method_name", "method_args", "queue", "periodic_job_id", "cron_expression", "error_count", "periodic_job_version", "batch_id", "awaits_batch_id", "continuation_condition", "originally_scheduled_at", "trace_parent", "previous_state", "state"
FROM "xact_jobs_job_history";

DROP TABLE "xact_jobs_job_history";

ALTER TABLE "xact_jobs_job_history_new" RENAME TO "xact_jobs_job_history";

CREATE INDEX "ix_job_history_processed_at" ON "xact_jobs_job_history" ("processed_at");
CREATE INDEX "ix_job_history_batch_id" ON "xact_jobs_job_history" ("batch_id") WHERE "batch_id" IS NOT NULL;
CREATE INDEX "ix_job_history_awaits_batch_id" ON "xact_jobs_job_history" ("awaits_batch_id") WHERE "awaits_batch_id" IS NOT NULL;

-- ============================
-- Table storing recurring jobs
-- ============================

-- The state the last completed run left for the next one (XactJobContext.PeriodicState), at most 2000 characters.
ALTER TABLE "xact_jobs_job_periodic" ADD COLUMN "state" TEXT NULL;
