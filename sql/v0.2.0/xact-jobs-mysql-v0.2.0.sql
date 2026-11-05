-- ============================
-- Table storing scheduled jobs
-- ============================

-- Batches and continuations
ALTER TABLE `xact_jobs_job`
  ADD COLUMN `batch_id` char(36) COLLATE ascii_general_ci NULL,
  ADD COLUMN `awaits_batch_id` char(36) COLLATE ascii_general_ci NULL,
  ADD COLUMN `continuation_condition` int NULL;

CREATE INDEX `ix_job_batch_id` ON `xact_jobs_job` (`batch_id`);
CREATE INDEX `ix_job_awaits_batch_id` ON `xact_jobs_job` (`awaits_batch_id`);

-- ===============================
-- Table storing execution history
-- ===============================

ALTER TABLE `xact_jobs_job_history`
  ADD COLUMN `batch_id` char(36) COLLATE ascii_general_ci NULL,
  ADD COLUMN `awaits_batch_id` char(36) COLLATE ascii_general_ci NULL,
  ADD COLUMN `continuation_condition` int NULL;

CREATE INDEX `ix_job_history_batch_id` ON `xact_jobs_job_history` (`batch_id`);
CREATE INDEX `ix_job_history_awaits_batch_id` ON `xact_jobs_job_history` (`awaits_batch_id`);
