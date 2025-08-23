
-- ============================
-- Table storing scheduled jobs
-- ============================

ALTER TABLE [XactJobs].[Job] ADD PeriodicJobVersion INT NULL;

-- ===============================
-- Table storing execution history
-- ===============================

ALTER TABLE [XactJobs].[JobHistory] ADD PeriodicJobVersion INT NULL;

-- ============================
-- Table storing recurring jobs
-- ============================

ALTER TABLE [XactJobs].[JobPeriodic] ADD Version INT NOT NULL CONSTRAINT DF_XactJobs_JobPeriodic_Version DEFAULT 1;
