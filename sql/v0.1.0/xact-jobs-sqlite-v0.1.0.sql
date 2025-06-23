-- ============================
-- Table storing scheduled jobs
-- ============================

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
  "error_count" INTEGER NOT NULL
);

CREATE INDEX "ix_job_leaser" ON "xact_jobs_job" ("leaser");

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
  "error_count" INTEGER NOT NULL
);

CREATE INDEX "ix_job_history_processed_at" ON "xact_jobs_job_history" ("processed_at");

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
  "is_active" INTEGER NOT NULL
);
