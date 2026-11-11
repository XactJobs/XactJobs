using System.Collections.Concurrent;

namespace XactJobs.Tests.PeriodicJobs;

/// <summary>
/// The only periodic jobs of this assembly, found by <c>WithPeriodicJobsFromAssemblyOf&lt;AttributeJobs&gt;()</c>. The isolated one
/// needs the queue <see cref="IsolatedQueue"/>.
/// </summary>
public class AttributeJobs
{
    public const string IsolatedQueue = "attr";

    /// <summary>
    /// The periodic job's id and the config of each run of <see cref="Run"/>.
    /// </summary>
    public static ConcurrentQueue<(string? PeriodicJobId, string? Config)> Runs { get; } = new();

    [XactPeriodicJob("attr_a", CronExpressions.EverySecond, Config = "a")]
    [XactPeriodicJob("attr_b", CronExpressions.EverySecond, Config = "b")]
    public void Run(string? config, XactJobContext context) => Runs.Enqueue((context.PeriodicJobId, config));

    [XactPeriodicJob("attr_static", CronExpressions.Daily)]
    public static Task CleanupAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [XactPeriodicJob("attr_isolated", CronExpressions.Daily, Queue = IsolatedQueue)]
    public Task OtherQueueAsync() => Task.CompletedTask;
}
