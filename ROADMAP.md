# Roadmap

XactJobs is feature complete for what it sets out to do: background jobs for EF Core that commit in the same
transaction as your data, in your own database. What's left before 1.0 is making the API a promise.

## Next

- **0.9.0**: retries per job method (`[XactRetry]`), job filters and failure handlers, `L`, `W` and `#` in cron
  expressions, and exclusions for periodic jobs.
- **1.0.0-rc1**: the public API is tracked in the repository, so any change to it shows in a pull request, and the
  packages are validated against the previous release. A release candidate is for trying in your apps: the API only
  changes for what you report.
- **1.0.0**: once the release candidate has had real use.

## After 1.0

[Semantic versioning](https://semver.org/) ([Versions](https://xactjobs.com/docs/upgrading/#versions) has the details):

- **Patch versions** fix bugs.
- **Minor versions** add features, and support for new .NET and EF Core versions. They don't break your code or
  your jobs, and a schema change comes with its upgrade script.
- **Major versions** may break the API, with an upgrade guide. Jobs already in your database keep running: the
  format a job is stored in doesn't change.

Supported versions, and when one is dropped, are on [Guarantees and limitations](https://xactjobs.com/docs/guarantees/#supported-versions).

## Not Planned

- **Storage other than your database** (Redis, a message broker). The point of XactJobs is one transaction with your
  data.
- **Apps without EF Core.**
- **A lock per job, or a limit on concurrent runs of a job type.** Jobs that must not run at the same time can lock
  the rows they work on, in the job's transaction. A periodic job never overlaps itself.

If something you need is missing, [open an issue](https://github.com/XactJobs/XactJobs/issues/new/choose) with what
you're trying to do.
