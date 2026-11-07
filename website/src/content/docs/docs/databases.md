---
title: Databases
description: Notes per database, from versions and providers to isolation levels, PostgreSQL autovacuum, Oracle 12c+, and several apps in one database.
---

XactJobs supports SQL Server, PostgreSQL, MySQL, Oracle and SQLite, with EF Core 9 on .NET 8 and 9, and EF Core 10 on .NET 10. Your
database provider has to support that version. The core is tested on all five databases, with an integration test suite running against
each one (SQLite in-process, the others in containers).

| Database | XactJobs' tables | Install script |
|---|---|---|
| SQL Server | `[XactJobs].[Job]`, `[XactJobs].[JobHistory]`, `[XactJobs].[JobPeriodic]` | [xact-jobs-sqlserver.sql](https://github.com/XactJobs/XactJobs/blob/main/sql/install/xact-jobs-sqlserver.sql) |
| PostgreSQL | `xact_jobs.job`, `xact_jobs.job_history`, `xact_jobs.job_periodic` | [xact-jobs-postgresql.sql](https://github.com/XactJobs/XactJobs/blob/main/sql/install/xact-jobs-postgresql.sql) |
| MySQL | `xact_jobs_job`, `xact_jobs_job_history`, `xact_jobs_job_periodic` | [xact-jobs-mysql.sql](https://github.com/XactJobs/XactJobs/blob/main/sql/install/xact-jobs-mysql.sql) |
| Oracle | `XACT_JOBS_JOB`, `XACT_JOBS_JOB_HISTORY`, `XACT_JOBS_JOB_PERIODIC` | [xact-jobs-oracle.sql](https://github.com/XactJobs/XactJobs/blob/main/sql/install/xact-jobs-oracle.sql) |
| SQLite | `xact_jobs_job`, `xact_jobs_job_history`, `xact_jobs_job_periodic` | [xact-jobs-sqlite.sql](https://github.com/XactJobs/XactJobs/blob/main/sql/install/xact-jobs-sqlite.sql) |

## SQL Server

Run the scripts with `QUOTED_IDENTIFIER` and `ANSI_NULLS` on, which are SqlClient's and SSMS's defaults (`sqlcmd` needs `-I`): the job
table has a filtered index. Writing to the table needs them on as well.

## PostgreSQL

The job table is a high-churn queue: every job inserts, updates and deletes a row. Aggressive autovacuum settings for this table are
recommended, for example:

```sql
ALTER TABLE xact_jobs.job SET (
  autovacuum_vacuum_scale_factor = 0,
  autovacuum_vacuum_threshold = 1000,
  autovacuum_vacuum_cost_limit = 2000
);
```

Long-running transactions **anywhere in the cluster** hold back the cleanup of dead rows, not just the ones using this table. Dead rows
pile up meanwhile, which slows down fetching jobs.

## MySQL

XactJobs needs **MySQL 8.0 or later**, because claiming jobs uses `SKIP LOCKED` and `FOR UPDATE OF`. MariaDB isn't supported: it doesn't
support `FOR UPDATE OF`, and only supports `SKIP LOCKED` from 10.6.

The workers claim jobs at `READ COMMITTED`, not at InnoDB's default `REPEATABLE READ`. At `REPEATABLE READ`, a claim would keep the running
jobs it scans locked, and the gaps between rows too, so finished jobs couldn't be deleted, and new jobs (`JobEnqueue` and `SaveChanges`)
couldn't be saved until the claim commits. Only the claims use it: your own transactions, and the jobs, keep their isolation level.

The provider depends on the version of .NET:

| .NET | MySQL provider |
|---|---|
| .NET 8 | `Pomelo.EntityFrameworkCore.MySql` |
| .NET 9 | `Pomelo.EntityFrameworkCore.MySql` or `MySql.EntityFrameworkCore` |
| .NET 10 | `MySql.EntityFrameworkCore` |

Pomelo doesn't support EF Core 10 yet, and on .NET 8 the official provider only supports EF Core 8. These combinations build, but fail at runtime.

**With the official provider (`MySql.EntityFrameworkCore`), `SaveChanges` doesn't start a transaction on its own.** If a statement fails,
the ones before it stay committed. This can break the main promise of XactJobs in your own code: for example, a job can be saved without
the business data it belongs to.

To make your saves all-or-nothing, either use a transaction, as in [Transactions](../scheduling/#transactions), or set this on your `DbContext`:

```csharp
public class UserDbContext : DbContext
{
    public UserDbContext(DbContextOptions<UserDbContext> options) : base(options)
    {
        Database.AutoTransactionBehavior = AutoTransactionBehavior.Always;
    }
}
```

The workers already do this for their own saves, so running jobs are not affected. Pomelo starts a transaction when a save has more than one statement.

## Oracle

XactJobs needs **Oracle 12c or later**. A claim is one PL/SQL block, which returns the claimed jobs with `DBMS_SQL.RETURN_RESULT`.

Jobs are claimed oldest first. The block locks the due jobs with `FOR UPDATE SKIP LOCKED`, fetches only as many as the worker has free
slots, and leases them. Oracle locks a row only when it's fetched, so workers that poll at once each get different jobs from a backlog.
`ROWNUM` can't be used to limit the rows, because it's applied before locked rows are skipped: all the workers would try the same first
rows, and all but one would get none.

The claims read the table's primary key in order (with an index hint), so a claim doesn't sort the whole backlog, even if Oracle cached a
plan from when the table was nearly empty.

## SQLite

On SQLite the primary key of the job table is only the id, so the claims use an index on the queue and the scheduled time. Without it,
every claim would read and sort the whole job table. The install script creates it; for an existing installation, the v0.3.0 script adds it.

A job enqueued with [QuickPoll](../queues-and-polling/#quickpoll) within the same millisecond as the poll can wait for the next regular poll.

## Several apps in one database

On PostgreSQL and SQL Server, XactJobs' tables are in their own schema: `xact_jobs` on PostgreSQL, `XactJobs` on SQL Server. Apps that
share a database each need their own tables, or their workers would run each other's jobs. Give each app a schema of its own:

```csharp
modelBuilder.ApplyXactJobsConfigurations(Database.ProviderName, schema: "app_b");
```

The workers take the schema from the model. With the SQL scripts, replace `xact_jobs` (or `XactJobs`) with your schema before you run
them; EF migrations use it on their own. On PostgreSQL the name must be lowercase.

On MySQL, Oracle and SQLite there's no schema option: give each app its own database (on Oracle, its own user), which already has its own tables.
