-- ============================
-- Table storing scheduled jobs
-- ============================
BEGIN TRANSACTION;

CREATE UNIQUE INDEX "uq_job_periodic_job_id" ON "xact_jobs_job" ("periodic_job_id", "periodic_job_version") WHERE periodic_job_id IS NOT NULL;

CREATE TABLE "ef_temp_xact_jobs_job" (
    "id" INTEGER NOT NULL CONSTRAINT "pk_job" PRIMARY KEY AUTOINCREMENT,
    "cron_expression" TEXT NULL,
    "error_count" INTEGER NOT NULL,
    "leased_until" DATETIME NULL,
    "leaser" TEXT NULL,
    "method_args" TEXT NOT NULL,
    "method_name" TEXT NOT NULL,
    "periodic_job_id" TEXT NULL,
    "periodic_job_version" INTEGER NULL,
    "queue" TEXT NOT NULL,
    "scheduled_at" DATETIME NOT NULL,
    "type_name" TEXT NOT NULL,
    CONSTRAINT "chk_job_periodic_job_id" CHECK (
      (periodic_job_id IS NULL AND periodic_job_version IS NULL)
        OR (periodic_job_id IS NOT NULL AND periodic_job_version IS NOT NULL)
    )
);

INSERT INTO "ef_temp_xact_jobs_job" ("id", "cron_expression", "error_count", "leased_until", "leaser", "method_args", "method_name", "periodic_job_id", "periodic_job_version", "queue", "scheduled_at", "type_name")
SELECT "id", "cron_expression", "error_count", "leased_until", "leaser", "method_args", "method_name", "periodic_job_id", "periodic_job_version", "queue", "scheduled_at", "type_name"
FROM "xact_jobs_job";

COMMIT;

BEGIN TRANSACTION;

DROP TABLE "xact_jobs_job";

ALTER TABLE "ef_temp_xact_jobs_job" RENAME TO "xact_jobs_job";

COMMIT;

BEGIN TRANSACTION;

CREATE INDEX "ix_job_leaser" ON "xact_jobs_job" ("leaser");

CREATE INDEX "ix_job_queue_scheduled_at" ON "xact_jobs_job" ("queue", "scheduled_at");

CREATE UNIQUE INDEX "uq_job_periodic_job_id" ON "xact_jobs_job" ("periodic_job_id", "periodic_job_version") WHERE periodic_job_id IS NOT NULL;

COMMIT;

-- ===============================
-- Table storing execution history
-- ===============================

-- ============================
-- Table storing recurring jobs
-- ============================

