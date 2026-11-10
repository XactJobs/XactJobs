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
