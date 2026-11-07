-- ============================
-- Table storing scheduled jobs
-- ============================

-- The time a job was originally scheduled for, kept by its retries (null: the same as scheduled_at)
ALTER TABLE "xact_jobs_job" ADD COLUMN "originally_scheduled_at" DATETIME NULL;

-- The claims look for a queue's due jobs, oldest first. The other databases use their primary key (queue, scheduled_at, id) for it,
-- but on SQLite the primary key is only the id: without this index, every claim reads and sorts the whole table.
CREATE INDEX "ix_job_queue_scheduled_at" ON "xact_jobs_job" ("queue", "scheduled_at");

-- ===============================
-- Table storing execution history
-- ===============================

ALTER TABLE "xact_jobs_job_history" ADD COLUMN "originally_scheduled_at" DATETIME NULL;
