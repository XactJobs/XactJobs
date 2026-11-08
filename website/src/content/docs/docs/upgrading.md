---
title: Upgrading
description: The SQL scripts per version and database, and what to do before and after each upgrade.
---

Schema changes ship as SQL scripts, one per version and database. Existing data is always kept. To upgrade an existing installation, run
the versioned scripts newer than its version, in version order, with the notes below. If you include the XactJobs entities in your
[EF migrations](../getting-started/#or-use-ef-migrations), add a migration instead.

For a new installation, run the install script instead: it creates the same schema as all the versioned scripts.

| Database | v0.1.0 | v0.1.4 | v0.1.5 | v0.2.0 | v0.3.0 | v0.4.0 |
|---|---|---|---|---|---|---|
| SQL Server | [script](https://github.com/XactJobs/XactJobs/blob/main/sql/v0.1.0/xact-jobs-sqlserver-v0.1.0.sql) | [script](https://github.com/XactJobs/XactJobs/blob/main/sql/v0.1.4/xact-jobs-sqlserver-v0.1.4.sql) | [script](https://github.com/XactJobs/XactJobs/blob/main/sql/v0.1.5/xact-jobs-sqlserver-v0.1.5.sql) | [script](https://github.com/XactJobs/XactJobs/blob/main/sql/v0.2.0/xact-jobs-sqlserver-v0.2.0.sql) | [script](https://github.com/XactJobs/XactJobs/blob/main/sql/v0.3.0/xact-jobs-sqlserver-v0.3.0.sql) | [script](https://github.com/XactJobs/XactJobs/blob/main/sql/v0.4.0/xact-jobs-sqlserver-v0.4.0.sql) |
| PostgreSQL | [script](https://github.com/XactJobs/XactJobs/blob/main/sql/v0.1.0/xact-jobs-postgresql-v0.1.0.sql) | [script](https://github.com/XactJobs/XactJobs/blob/main/sql/v0.1.4/xact-jobs-postgresql-v0.1.4.sql) | [script](https://github.com/XactJobs/XactJobs/blob/main/sql/v0.1.5/xact-jobs-postgresql-v0.1.5.sql) | [script](https://github.com/XactJobs/XactJobs/blob/main/sql/v0.2.0/xact-jobs-postgresql-v0.2.0.sql) | [script](https://github.com/XactJobs/XactJobs/blob/main/sql/v0.3.0/xact-jobs-postgresql-v0.3.0.sql) | [script](https://github.com/XactJobs/XactJobs/blob/main/sql/v0.4.0/xact-jobs-postgresql-v0.4.0.sql) |
| MySQL | [script](https://github.com/XactJobs/XactJobs/blob/main/sql/v0.1.0/xact-jobs-mysql-v0.1.0.sql) | [script](https://github.com/XactJobs/XactJobs/blob/main/sql/v0.1.4/xact-jobs-mysql-v0.1.4.sql) | [script](https://github.com/XactJobs/XactJobs/blob/main/sql/v0.1.5/xact-jobs-mysql-v0.1.5.sql) | [script](https://github.com/XactJobs/XactJobs/blob/main/sql/v0.2.0/xact-jobs-mysql-v0.2.0.sql) | [script](https://github.com/XactJobs/XactJobs/blob/main/sql/v0.3.0/xact-jobs-mysql-v0.3.0.sql) | none |
| Oracle | [script](https://github.com/XactJobs/XactJobs/blob/main/sql/v0.1.0/xact-jobs-oracle-v0.1.0.sql) | [script](https://github.com/XactJobs/XactJobs/blob/main/sql/v0.1.4/xact-jobs-oracle-v0.1.4.sql) | [script](https://github.com/XactJobs/XactJobs/blob/main/sql/v0.1.5/xact-jobs-oracle-v0.1.5.sql) | [script](https://github.com/XactJobs/XactJobs/blob/main/sql/v0.2.0/xact-jobs-oracle-v0.2.0.sql) | [script](https://github.com/XactJobs/XactJobs/blob/main/sql/v0.3.0/xact-jobs-oracle-v0.3.0.sql) | none |
| SQLite | [script](https://github.com/XactJobs/XactJobs/blob/main/sql/v0.1.0/xact-jobs-sqlite-v0.1.0.sql) | [script](https://github.com/XactJobs/XactJobs/blob/main/sql/v0.1.4/xact-jobs-sqlite-v0.1.4.sql) | [script](https://github.com/XactJobs/XactJobs/blob/main/sql/v0.1.5/xact-jobs-sqlite-v0.1.5.sql) | [script](https://github.com/XactJobs/XactJobs/blob/main/sql/v0.2.0/xact-jobs-sqlite-v0.2.0.sql) | [script](https://github.com/XactJobs/XactJobs/blob/main/sql/v0.3.0/xact-jobs-sqlite-v0.3.0.sql) | [script](https://github.com/XactJobs/XactJobs/blob/main/sql/v0.4.0/xact-jobs-sqlite-v0.4.0.sql) |

On SQL Server, run the scripts with `QUOTED_IDENTIFIER` and `ANSI_NULLS` on, which are SqlClient's and SSMS's defaults (`sqlcmd` needs `-I`).

Until 1.0 the API can still change between minor versions (0.x). Breaking changes are listed in the
[release notes](https://github.com/XactJobs/XactJobs/releases), with how to migrate.

## v0.4.0

The script can run before or after the workers are upgraded. On PostgreSQL, SQL Server and SQLite, it rebuilds the indexes of the batch
columns, in the job table and the history, so that they hold only the rows of batches and continuations: the other jobs' inserts and
deletes don't write to them. There's no script for MySQL, which has no filtered indexes, nor for Oracle, whose indexes already leave out
those rows.

## v0.3.0

Run the script first: it adds the column for the time a job was originally scheduled for, which v0.3.0 needs. Then upgrade the workers.
A worker of an older version that retries a job leaves the column empty, so that retry sees its own retry time as
`XactJobContext.OriginallyScheduledAt`.

- On SQLite, the script also adds an index on the queue and the scheduled time, for the claims: without it, every claim reads and sorts
  the whole job table.
- Except on MySQL, it also adds an index of the periodic job column, with only the runs of periodic jobs, so that pausing a periodic job,
  resuming it, the reconciliation and the dashboard's Periodic page don't read the whole table.
- On SQL Server it changes the column from `nvarchar(max)`, which can't be indexed, to `nvarchar(450)`, the type of the periodic job's
  id, and it's a filtered index: creating it, and writing to the table, needs `QUOTED_IDENTIFIER` and `ANSI_NULLS` on, which are
  SqlClient's defaults (`sqlcmd` needs `-I`).
- The dashboard's API identifies a pending job by its primary key now: see [API](../dashboard/#api).

## v0.2.0

Run the script first: it adds the columns for batches and continuations, which v0.2.0 needs. Start using batches and continuations only
once all the workers run v0.2.0: a worker of an older version that retries a job of a batch removes the job from its batch, so the batch's
continuations could start too early.

## v0.1.5

Upgrade all the workers first and then run the script, because it drops an index that older versions need.
