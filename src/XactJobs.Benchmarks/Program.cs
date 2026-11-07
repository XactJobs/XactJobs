using BenchmarkDotNet.Running;
using XactJobs.Benchmarks.Drain;

namespace XactJobs.Benchmarks;

public static class Program
{
    private const string Usage = """
        Usage:
          micro [BenchmarkDotNet options]   the code paths without a database (e.g. --filter "*Enqueue*", --job short, --inProcess)
          drain [--jobs N] [--runs N] [--parallelism N] [--sql] [--only xactjobs|ef]
                                            drains a backlog on SQLite in memory, with XactJobs and with plain EF, and prints the difference
                                            (--sql lists the SQL commands per job, --only runs one of them, e.g. to profile it)
        """;

    public static async Task<int> Main(string[] args)
    {
        switch (args.FirstOrDefault())
        {
            case "micro":
                BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args[1..]);
                return 0;

            case "drain":
                return await DrainBenchmark.RunAsync(args[1..]);

            default:
                Console.WriteLine(Usage);
                return 1;
        }
    }
}
