namespace XactJobs.TestWeb;

public class SampleJobs
{
    private readonly ILogger<SampleJobs> _logger;

    public SampleJobs(ILogger<SampleJobs> logger)
    {
        _logger = logger;
    }

    public void QuickJob()
    {
        _logger.LogInformation("Quick job executed at {Time}", DateTime.UtcNow);
    }

    public async Task SlowJobAsync(int delayMs, CancellationToken ct)
    {
        _logger.LogInformation("Slow job starting, will take {Delay}ms", delayMs);
        await Task.Delay(delayMs, ct);
        _logger.LogInformation("Slow job completed");
    }

    public void FailingJob()
    {
        throw new InvalidOperationException("This job is designed to fail!");
    }

    public static void StaticJob(string message)
    {
        Console.WriteLine($"[StaticJob] {message} at {DateTime.UtcNow}");
    }
}
