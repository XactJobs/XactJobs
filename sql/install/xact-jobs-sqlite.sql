-- XactJobs v0.5.0, SQLite: the whole schema, for a new installation.
-- It's the same as running the versioned scripts (sql/v*/) in order. To upgrade an existing installation, run only the versioned
-- scripts newer than its version.

-- ============================
-- Table storing scheduled jobs
-- ============================

-- SQLite only generates ids for an INTEGER PRIMARY KEY, so the primary key is only the id (the other databases' is queue, scheduled_at, id)
CREATE TABLE "xact_jobs_job" (
  "id" INTEGER NOT NULL CONSTRAINT "pk_job" PRIMARY KEY AUTOINCREMENT,
  "leased_until" DATETIME NULL,
  "leaser" TEXT NULL,
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
  -- the time a job was originally scheduled for, kept by its retries (null: the same as scheduled_at)
  "originally_scheduled_at" DATETIME NULL,
  -- the W3C traceparent of the activity that enqueued the job, if any: its attempts' activities link to that trace
  "trace_parent" TEXT NULL
);

-- batches and continuations: only the rows of a batch, or of a continuation (most jobs are in none)
CREATE INDEX "ix_job_batch_id" ON "xact_jobs_job" ("batch_id") WHERE "batch_id" IS NOT NULL;
CREATE INDEX "ix_job_awaits_batch_id" ON "xact_jobs_job" ("awaits_batch_id") WHERE "awaits_batch_id" IS NOT NULL;

-- the claims look for a queue's due jobs, oldest first: without this index, every claim reads and sorts the whole table
CREATE INDEX "ix_job_queue_scheduled_at" ON "xact_jobs_job" ("queue", "scheduled_at");

-- the runs of a periodic job: only the rows of periodic jobs, so the other jobs' inserts and deletes don't write to it
CREATE INDEX "ix_job_periodic_job_id" ON "xact_jobs_job" ("periodic_job_id") WHERE "periodic_job_id" IS NOT NULL;

-- ===============================
-- Table storing execution history
-- ===============================

CREATE TABLE "xact_jobs_job_history" (
  "id" INTEGER NOT NULL CONSTRAINT "pk_job_history" PRIMARY KEY,
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
  "trace_parent" TEXT NULL
);

CREATE INDEX "ix_job_history_processed_at" ON "xact_jobs_job_history" ("processed_at");
CREATE INDEX "ix_job_history_batch_id" ON "xact_jobs_job_history" ("batch_id") WHERE "batch_id" IS NOT NULL;
CREATE INDEX "ix_job_history_awaits_batch_id" ON "xact_jobs_job_history" ("awaits_batch_id") WHERE "awaits_batch_id" IS NOT NULL;

-- ============================
-- Table storing recurring jobs
-- ============================

CREATE TABLE "xact_jobs_job_periodic" (
  "id" TEXT NOT NULL CONSTRAINT "pk_job_periodic" PRIMARY KEY,
  "created_at" DATETIME NOT NULL,
  "updated_at" DATETIME NOT NULL,
  "cron_expression" TEXT NOT NULL,
  "type_name" TEXT NOT NULL,
  "method_name" TEXT NOT NULL,
  "method_args" TEXT NOT NULL,
  "queue" TEXT NOT NULL,
  "is_active" INTEGER NOT NULL,
  "version" INTEGER NOT NULL DEFAULT 1
);
