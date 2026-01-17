
-- ============================
-- Table storing scheduled jobs
-- ============================

-- needed to change from nvarchar(max) so it can be a part of an index below
ALTER TABLE [XactJobs].[Job] ALTER COLUMN [PeriodicJobId] nvarchar(450) NULL;

CREATE UNIQUE INDEX [UQ_Job_PeriodicJobId] ON [XactJobs].[Job] ([PeriodicJobId], [PeriodicJobVersion])
  WHERE [PeriodicJobId] IS NOT NULL;

ALTER TABLE [XactJobs].[Job] ADD CONSTRAINT [CHK_Job_PeriodicJobId] CHECK (
  ([PeriodicJobId] IS NULL AND [PeriodicJobVersion] IS NULL)
    OR ([PeriodicJobId] IS NOT NULL AND [PeriodicJobVersion] IS NOT NULL)
);

-- ===============================
-- Table storing execution history
-- ===============================

-- ============================
-- Table storing recurring jobs
-- ============================
