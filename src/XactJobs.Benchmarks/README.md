# XactJobs.Benchmarks

Benchmarks of XactJobs' own code, to catch regressions between commits. They're not numbers for comparing XactJobs with other libraries or databases: SQLite in memory has no network, and a single writer.

Compare two commits on the same machine, one run at a time, with nothing else running. Always build in Release.

```bash
dotnet build -c Release src/XactJobs.Benchmarks/XactJobs.Benchmarks.csproj
```

## micro: without a database

BenchmarkDotNet, with the allocations per call:

- `EnqueueBenchmarks` - what `JobEnqueue` does before EF adds the row: the expression tree (as the compiler builds it at the call site), evaluating the arguments, and serializing them
- `RunBenchmarks` - what the worker does to run a job: reading the arguments, finding the compiled delegate, and calling it
- `CronBenchmarks` - the next run of a periodic job
- `SaveCheckBenchmarks` - subscribing and unsubscribing the check of saves outside a transaction, for each job

```bash
dotnet run --no-build -c Release -f net10.0 --project src/XactJobs.Benchmarks -- micro --filter "*"
# quicker and lighter, e.g. on a small VM: fewer iterations, and no separate process per benchmark
dotnet run --no-build -c Release -f net10.0 --project src/XactJobs.Benchmarks -- micro --filter "*Run*" --job short --inProcess
```

The allocations are the most stable numbers: they barely change between runs, even on a busy machine.

## drain: the worker on SQLite, compared with plain EF

Drains a backlog of jobs that do nothing, twice, on SQLite in memory:

- with XactJobs: a host with one worker;
- with plain EF: the same claim, then for each job its delete and its history row, saved together. That's the least database work for the same result.

With `--batch-size` at or below `--parallelism`, where both pick up only the jobs they can start, the difference per job is XactJobs' own cost: scopes, the runner's bookkeeping, running the job. By default XactJobs picks up batches of twice the parallelism and plain EF one job at a time, so the `XactJobs overhead` row also shows what batching saves; the drain says so under the results. Both are measured from the first completed job, because a worker's first poll waits for its polling interval.

```bash
dotnet run --no-build -c Release -f net10.0 --project src/XactJobs.Benchmarks -- drain --jobs 3000 --runs 5

# XactJobs' own cost: no batch, as plain EF
dotnet run --no-build -c Release -f net10.0 --project src/XactJobs.Benchmarks -- drain --jobs 3000 --runs 5 --batch-size 1
```

```
per job                 jobs/s   wall µs    CPU µs       KB     SQL    SQL µs
XactJobs                   ...
plain EF                   ...
XactJobs overhead                    ...
```

- `CPU µs` is the CPU time of the whole process, on all threads, SQLite's included. `KB` is the bytes allocated on all threads.
- `SQL` and `SQL µs` are the SQL commands per job and the time EF measured for them. They should be the same in both: if XactJobs runs more SQL, it shows here.
- `--sql` lists each command, with its count and time per job.
- `--only xactjobs` or `--only ef` runs one of them, e.g. to profile it with `dotnet-trace`.
- `--workers N` runs N workers, and `--parallelism N` N jobs at a time per worker. Plain EF gets as many slots in all, each claiming one job when it's free. On SQLite the claims and the saves then wait for each other, so this measures SQLite's single writer more than XactJobs: use PostgreSQL.
- `--batch-size N` makes the XactJobs workers pick up batches of N jobs (`WithBatchSize`; by default twice the parallelism, and at or below the parallelism, no batch). Plain EF never batches.
- `--context-pool` registers the context with `AddDbContextPool` instead of `AddDbContext`, for both XactJobs and plain EF. On PostgreSQL (2 workers × 4, batch 100) it made XactJobs about 20% faster, with 60% less allocated per job: most of a job's own cost is creating its context.
- `--log` shows XactJobs' warnings and errors, e.g. why a job failed. A failed attempt doesn't end the drain: only completions count, and the failed attempts are printed.
- `--postgres <connection string>` drains on PostgreSQL instead. Each drain creates its own database on that server, named `bench_` and a random id (e.g. `bench_3f2a9c…`), with the schema from the install script and fresh statistics, and drops it at the end. The database in the connection string (`postgres` below) is only used to connect: the tables never appear there. A drain stopped with Ctrl+C leaves its database behind: drop it by hand. With several workers, XactJobs is measured once all of them poll (each one's first poll waits for its polling interval). For example, with a container:

  ```bash
  docker run -d --name xactjobs-bench-pg -e POSTGRES_PASSWORD=bench -p 127.0.0.1:5432:5432 postgres:16.15
  dotnet run --no-build -c Release -f net10.0 --project src/XactJobs.Benchmarks -- drain --jobs 10000 --runs 3 --workers 2 --parallelism 10 \
      --postgres "Host=127.0.0.1;Username=postgres;Password=bench;Database=postgres"
  docker rm -f xactjobs-bench-pg
  ```

  The database's CPU time isn't in `CPU µs`, which is the benchmark process's only.

  On Linux, a port published with `-p` goes through `docker-proxy`, a process that copies every byte between the benchmark and the container. On a 4-vCPU machine it took almost half a core, and the drain was 15–30% slower than when connecting to the container's own address, which skips it:

  ```bash
  docker run -d --name xactjobs-bench-pg -e POSTGRES_PASSWORD=bench postgres:16.15
  IP=$(docker inspect -f '{{range .NetworkSettings.Networks}}{{.IPAddress}}{{end}}' xactjobs-bench-pg)
  dotnet run --no-build -c Release -f net10.0 --project src/XactJobs.Benchmarks -- drain --jobs 10000 --runs 3 --workers 2 --parallelism 10 \
      --postgres "Host=$IP;Username=postgres;Password=bench;Database=postgres"
  ```

  The same applies to SQL Server. Compare commits with the same setup.
- `--sqlserver <connection string>` does the same on SQL Server: a new `bench_…` database for each drain, dropped at the end, whatever database the connection string names. For example:

  ```bash
  docker run -d --name xactjobs-bench-mssql -e ACCEPT_EULA=Y -e 'MSSQL_SA_PASSWORD=Bench_pass1!' -e MSSQL_MEMORY_LIMIT_MB=2048 \
      -p 127.0.0.1:1433:1433 mcr.microsoft.com/mssql/server:2022-CU27-GDR1-ubuntu-22.04
  dotnet run --no-build -c Release -f net10.0 --project src/XactJobs.Benchmarks -- drain --jobs 10000 --runs 3 --workers 2 --parallelism 4 \
      --sqlserver "Server=127.0.0.1;User ID=sa;Password=Bench_pass1!;TrustServerCertificate=True"
  docker rm -f xactjobs-bench-mssql
  ```
- `--mysql <connection string>` does the same on MySQL, with the official provider (MySql.EntityFrameworkCore): a new `bench_…` database for each drain, dropped at the end. The user needs to create and drop databases. For example:

  ```bash
  docker run -d --name xactjobs-bench-mysql -e MYSQL_ROOT_PASSWORD=bench -p 127.0.0.1:3306:3306 mysql:8.4.13
  dotnet run --no-build -c Release -f net10.0 --project src/XactJobs.Benchmarks -- drain --jobs 10000 --runs 3 --workers 2 --parallelism 4 \
      --mysql "Server=127.0.0.1;User ID=root;Password=bench"
  docker rm -f xactjobs-bench-mysql
  ```
