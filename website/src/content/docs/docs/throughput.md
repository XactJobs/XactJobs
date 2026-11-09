---
title: Throughput and benchmarks
description: A measured drain rate on a small cloud VM, and the benchmarks in the repository to measure on your own machine.
---

## Throughput

On a Hetzner Cloud CX33 (cost-optimized, 4 shared vCPUs, AMD EPYC Rome, 8 GB of RAM), with PostgreSQL 16 in a container on the same
machine (its default settings, so every commit waits for the disk), .NET 10 and EF Core 10, XactJobs drained a backlog of 20,000 no-op
jobs at **2,300 to 2,600 jobs per second**. The best setting was 2 workers, `MaxDegreeOfParallelism` 4 and `WithBatchSize(100)`.

Each job is still its own transaction: its changes, the delete of its row and its history row commit together.

With real jobs, their own work sets the pace. A larger batch means fewer polls, which helps short jobs, at a cost for slow ones: see [Batch
size](../queues-and-polling/#batch-size).

## Benchmarks

The benchmarks are in [src/XactJobs.Benchmarks](https://github.com/XactJobs/XactJobs/tree/main/src/XactJobs.Benchmarks), to measure on
your own machine. They measure XactJobs' own code, to catch regressions between commits: compare two commits on the same machine, one run
at a time, with nothing else running. Always build in Release.

```bash
dotnet build -c Release src/XactJobs.Benchmarks/XactJobs.Benchmarks.csproj
```

### micro: without a database

BenchmarkDotNet, with the allocations per call: enqueueing (the expression tree, evaluating the arguments and serializing them), running a
job (reading the arguments, finding the compiled delegate and calling it), the next run of a periodic job, and the save check.

```bash
dotnet run --no-build -c Release -f net10.0 --project src/XactJobs.Benchmarks -- micro --filter "*"
```

### drain: a backlog of no-op jobs

Drains a backlog of jobs that do nothing, with XactJobs and with plain EF doing the least database work for the same result (the same query
to pick up jobs, then for each job its delete and its history row, saved together). The difference per job is XactJobs' own cost. It runs on
SQLite in memory by default, or on a PostgreSQL or SQL Server server:

```bash
dotnet run --no-build -c Release -f net10.0 --project src/XactJobs.Benchmarks -- drain --jobs 3000 --runs 5

dotnet run --no-build -c Release -f net10.0 --project src/XactJobs.Benchmarks -- drain --jobs 10000 --runs 3 --workers 2 --parallelism 4 \
    --batch-size 100 --postgres "Host=127.0.0.1;Username=postgres;Password=bench;Database=postgres"
```

`--sqlserver <connection string>` runs it on SQL Server. With either, the connection string is only used to connect to the server: each
drain creates a new database there, named `bench_` and a random id (e.g. `bench_3f2a9c…`), with the schema from the install script, and
drops it at the end. The database named in the connection string (`postgres` above) isn't used, so the tables are only in the `bench_…`
database while a drain runs. A drain stopped with Ctrl+C leaves its database behind: drop it by hand. The [benchmark
README](https://github.com/XactJobs/XactJobs/tree/main/src/XactJobs.Benchmarks) has all the options, and how to start the database in a
container.
