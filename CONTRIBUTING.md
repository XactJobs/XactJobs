# Contributing

Bug reports, fixes and docs improvements are welcome. For a new feature, open an issue first, so we can agree on
what it does before you write it: XactJobs keeps a small API on purpose (see the [roadmap](ROADMAP.md)).

## Building

You need the .NET 8, 9 and 10 SDKs. The tests of real databases need Docker.

```bash
dotnet build src/XactJobs.sln
```

Warnings fail the build (`TreatWarningsAsErrors`), and the threading analyzers are on: async methods end with
`Async` and return a `Task`.

## Testing

The tests in `src/XactJobs.Tests/Databases/` and `src/XactJobs.Tests/Postgres/` run against PostgreSQL 16, 17 and 18,
SQL Server, MySQL and Oracle in containers (Testcontainers), and skip themselves without Docker. The first run
downloads the images, about 5 GB.

`dotnet test src/XactJobs.sln` runs everything at once: both frameworks, and several database containers in parallel.
On a machine with less than 16 GB of RAM, run one framework and one database at a time:

```bash
dotnet build src/XactJobs.Tests/XactJobs.Tests.csproj

# SQLite, the serializer, the cron parser and the rest that needs no Docker (and the dashboard's tests)
dotnet test --no-build -f net9.0 src/XactJobs.Tests/XactJobs.Tests.csproj --filter "FullyQualifiedName!~XactJobs.Tests.Databases&FullyQualifiedName!~XactJobs.Tests.Postgres"
dotnet test -f net9.0 src/XactJobs.UI.Tests/XactJobs.UI.Tests.csproj

# then one database: Postgres16, Postgres17, Postgres18, OfficialMySql, PomeloMySql (net9.0 only), SqlServer, Oracle
dotnet test --no-build -f net9.0 src/XactJobs.Tests/XactJobs.Tests.csproj --filter "FullyQualifiedName~XactJobs.Tests.Databases.SqlServer"

# and the PostgreSQL-only tests of one version
dotnet test --no-build -f net9.0 src/XactJobs.Tests/XactJobs.Tests.csproj --filter "FullyQualifiedName~XactJobs.Tests.Postgres.Postgres16"
```

Then the same with `-f net10.0` (EF Core 10). A pull request runs all of them on GitHub, each suite on its own runner,
so it's fine to run only the databases your change touches.

A fix comes with a test that fails without it. A change to the behavior of the workers, the claims or the SQL needs
its test in `Databases/`, so it runs on every database.

## What a Change Includes

- **Docs.** The README and the docs pages (`website/src/content/docs/docs/`) follow the code: a change to the API, an
  option, a default or a SQL script updates them, and their code samples must compile against the current API.
  `cd website && npm ci && npm run build` builds the website (Node.js 22.23 or later).
- **Schema changes.** A new `sql/v<version>/` script per database (released scripts never change), the same change in
  `sql/install/`, and in the EF model. The tests check that the install script, the versioned scripts and the model
  create the same schema. Add the upgrade notes to `website/src/content/docs/docs/upgrading.md`.
- **The stored format of a job never changes.** Existing rows have to keep running: `Golden/SerializerFormatTests`
  has the exact strings.
- **Log messages and metric names are a contract**: dashboards and alerts depend on them.
- **The public API is listed** in `PublicAPI.Shipped.txt` and `PublicAPI.Unshipped.txt` next to each package's project, and the build
  fails until a change to it is added to `PublicAPI.Unshipped.txt` (Visual Studio and Rider offer the fix, or run
  `dotnet format analyzers src/XactJobs/XactJobs.csproj --diagnostics RS0016 --severity info`). Keep new types internal unless users
  need them. A package is also compared with the last release when it's built (package validation): a removed or changed API fails
  the build. See [Versions](https://xactjobs.com/docs/upgrading/#versions).
- New source files start with the MIT header:

  ```csharp
  // Copyright (c) 2025 XactJobs
  // Licensed under the MIT License. See LICENSE in the repository root.
  ```

[AGENTS.md](AGENTS.md) describes the architecture and the reasons behind its less obvious parts, for people and for
coding agents alike.

## Pull Requests

One change per pull request, with a description of what it changes and why. By contributing, you agree that your
contribution is licensed under the [MIT License](LICENSE).

## Security

Please report vulnerabilities privately: see [SECURITY.md](SECURITY.md).
