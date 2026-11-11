-- ============================
-- Table storing scheduled jobs
-- ============================

-- One pending run per periodic job and version. Runs from before v0.1.4 have no version, which counts as 1: they get it, so that
-- the index treats them like the others. If a periodic job already has two runs of one version, creating the index fails: run the
-- script so that it stops at the first error, find them with:
--   SELECT [PeriodicJobId], [PeriodicJobVersion], COUNT(*) FROM [XactJobs].[Job] WHERE [PeriodicJobId] IS NOT NULL
--     GROUP BY [PeriodicJobId], [PeriodicJobVersion] HAVING COUNT(*) > 1;
-- delete the extra runs, and run the script again.

UPDATE [XactJobs].[Job] SET [PeriodicJobVersion] = 1 WHERE [PeriodicJobId] IS NOT NULL AND [PeriodicJobVersion] IS NULL;

CREATE UNIQUE INDEX [IX_Job_PeriodicJobId_Version] ON [XactJobs].[Job] ([PeriodicJobId], [PeriodicJobVersion]) WHERE [PeriodicJobId] IS NOT NULL;

-- the new index starts with the same column
DROP INDEX [IX_Job_PeriodicJobId] ON [XactJobs].[Job];

-- ===============================
-- Table storing execution history
-- ===============================

-- A periodic run's state: the one it started with, and the one after it (the same, unless it completed and set one).
ALTER TABLE [XactJobs].[JobHistory] ADD
  [PreviousState] nvarchar(2000) NULL,
  [State] nvarchar(2000) NULL;

-- ============================
-- Table storing recurring jobs
-- ============================

-- The state the last completed run left for the next one (XactJobContext.PeriodicState), at most 2000 characters.
ALTER TABLE [XactJobs].[JobPeriodic] ADD
  [State] nvarchar(2000) NULL;
