-- XactJobs v0.6.0, MySQL: the whole schema, for a new installation.
-- It's the same as running the versioned scripts (sql/v*/) in order. To upgrade an existing installation, run only the versioned
-- scripts newer than its version.

-- ============================
-- Table storing scheduled jobs
-- ============================

-- MySQL requires a key starting with the auto increment column, which isn't the first one of the primary key: UNIQUE (id)
CREATE TABLE `xact_jobs_job` (
  `id` bigint NOT NULL AUTO_INCREMENT,
  `scheduled_at` datetime(6) NOT NULL,
  `queue` varchar(50) CHARACTER SET utf8mb4 NOT NULL,
  `leased_until` datetime(6) NULL,
  `leaser` char(36) COLLATE ascii_general_ci NULL,
  `type_name` longtext CHARACTER SET utf8mb4 NOT NULL,
  `method_name` longtext CHARACTER SET utf8mb4 NOT NULL,
  `method_args` longtext CHARACTER SET utf8mb4 NOT NULL,
  `periodic_job_id` longtext CHARACTER SET utf8mb4 NULL,
  `cron_expression` longtext CHARACTER SET utf8mb4 NULL,
  `error_count` int NOT NULL,
  `periodic_job_version` int NULL,
  `batch_id` char(36) COLLATE ascii_general_ci NULL,
  `awaits_batch_id` char(36) COLLATE ascii_general_ci NULL,
  `continuation_condition` int NULL,
  -- the time a job was originally scheduled for, kept by its retries (null: the same as scheduled_at)
  `originally_scheduled_at` datetime(6) NULL,
  -- the W3C traceparent of the activity that enqueued the job, if any: its attempts' activities link to that trace
  `trace_parent` longtext CHARACTER SET utf8mb4 NULL,
  CONSTRAINT `pk_job` PRIMARY KEY (`queue`, `scheduled_at`, `id`),
  UNIQUE (`id`)
) CHARACTER SET=utf8mb4;

-- batches and continuations
CREATE INDEX `ix_job_batch_id` ON `xact_jobs_job` (`batch_id`);
CREATE INDEX `ix_job_awaits_batch_id` ON `xact_jobs_job` (`awaits_batch_id`);

-- ===============================
-- Table storing execution history
-- ===============================

CREATE TABLE `xact_jobs_job_history` (
  `id` bigint NOT NULL,
  `processed_at` datetime(6) NOT NULL,
  `status` int NOT NULL,
  `error_message` longtext CHARACTER SET utf8mb4 NULL,
  `error_stack_trace` longtext CHARACTER SET utf8mb4 NULL,
  `scheduled_at` datetime(6) NOT NULL,
  `type_name` longtext CHARACTER SET utf8mb4 NOT NULL,
  `method_name` longtext CHARACTER SET utf8mb4 NOT NULL,
  `method_args` longtext CHARACTER SET utf8mb4 NOT NULL,
  `queue` longtext CHARACTER SET utf8mb4 NOT NULL,
  `periodic_job_id` longtext CHARACTER SET utf8mb4 NULL,
  `cron_expression` longtext CHARACTER SET utf8mb4 NULL,
  `error_count` int NOT NULL,
  `periodic_job_version` int NULL,
  `batch_id` char(36) COLLATE ascii_general_ci NULL,
  `awaits_batch_id` char(36) COLLATE ascii_general_ci NULL,
  `continuation_condition` int NULL,
  `originally_scheduled_at` datetime(6) NULL,
  -- the W3C traceparent of the activity that enqueued the job, if any: its attempts' activities link to that trace
  `trace_parent` longtext CHARACTER SET utf8mb4 NULL,
  -- a periodic run's state: the one it started with, and the one after it (the same, unless it completed and set one)
  `previous_state` varchar(2000) CHARACTER SET utf8mb4 NULL,
  `state` varchar(2000) CHARACTER SET utf8mb4 NULL,
  CONSTRAINT `pk_job_history` PRIMARY KEY (`id`, `processed_at`)
) CHARACTER SET=utf8mb4;

CREATE INDEX `ix_job_history_processed_at` ON `xact_jobs_job_history` (`processed_at`);
CREATE INDEX `ix_job_history_batch_id` ON `xact_jobs_job_history` (`batch_id`);
CREATE INDEX `ix_job_history_awaits_batch_id` ON `xact_jobs_job_history` (`awaits_batch_id`);

-- ============================
-- Table storing recurring jobs
-- ============================

CREATE TABLE `xact_jobs_job_periodic` (
  `id` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
  `created_at` datetime(6) NOT NULL,
  `updated_at` datetime(6) NOT NULL,
  `cron_expression` longtext CHARACTER SET utf8mb4 NOT NULL,
  `type_name` longtext CHARACTER SET utf8mb4 NOT NULL,
  `method_name` longtext CHARACTER SET utf8mb4 NOT NULL,
  `method_args` longtext CHARACTER SET utf8mb4 NOT NULL,
  `queue` longtext CHARACTER SET utf8mb4 NOT NULL,
  `is_active` tinyint NOT NULL,
  `version` int NOT NULL DEFAULT 1,
  -- the state the last completed run left for the next one (XactJobContext.PeriodicState), at most 2000 characters
  `state` varchar(2000) CHARACTER SET utf8mb4 NULL,
  CONSTRAINT `pk_job_periodic` PRIMARY KEY (`id`)
) CHARACTER SET=utf8mb4;
