namespace XactJobs.Benchmarks;

public sealed record Order(int Id, string Customer, decimal Total, string[] Lines);

public sealed class JobDependency
{
}

/// <summary>
/// Jobs that do nothing, so a benchmark measures only the code around them.
/// </summary>
public sealed class BenchmarkJobs(JobDependency dependency)
{
    public static void Noop()
    {
    }

    public static void Send(string to, int orderId)
    {
    }

    public static void Process(Order order)
    {
    }

    public Task RunAsync(int orderId, XactJobContext context, CancellationToken cancellationToken)
    {
        GC.KeepAlive(dependency);

        return Task.CompletedTask;
    }
}
