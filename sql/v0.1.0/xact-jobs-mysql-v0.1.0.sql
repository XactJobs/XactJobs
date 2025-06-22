-- ============================
-- Table storing scheduled jobs
-- ============================

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
  CONSTRAINT `pk_job` PRIMARY KEY (`queue`, `scheduled_at`, `id`),
  UNIQUE (`id`)
) CHARACTER SET=utf8mb4;

CREATE INDEX `ix_job_leaser` ON `xact_jobs_job` (`leaser`);

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
  CONSTRAINT `pk_job_history` PRIMARY KEY (`id`, `processed_at`)
) CHARACTER SET=utf8mb4;

CREATE INDEX `ix_job_history_processed_at` ON `xact_jobs_job_history` (`processed_at`);

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
  CONSTRAINT `pk_job_periodic` PRIMARY KEY (`id`)
) CHARACTER SET=utf8mb4;
