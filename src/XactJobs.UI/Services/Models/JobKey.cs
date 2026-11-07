using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace XactJobs.UI.Services.Models;

/// <summary>
/// The primary key of a pending job's row. The dashboard and its API identify a pending job by it, not by its id alone: on
/// PostgreSQL, SQL Server and Oracle the primary key is (queue, scheduled time, id), with no index on the id alone, so a lookup
/// by id would read the whole job table.
/// </summary>
public sealed record JobKey(string Queue, DateTime ScheduledAt, long Id)
{
    public static JobKey Of(XactJob job) => new(job.Queue, job.ScheduledAt, job.Id);

    public static JobKey Of(JobDto job) => new(job.Queue, job.ScheduledAt, job.Id);

    /// <summary>
    /// The query string of the API's routes of a job (<c>/jobs/{id}</c>): <c>?queue=...&amp;scheduledAt=...</c>.
    /// </summary>
    public string ToQueryString() => $"?queue={Uri.EscapeDataString(Queue)}&scheduledAt={Uri.EscapeDataString(FormatTime(ScheduledAt))}";

    /// <summary>
    /// The key from the API's route and query string. The scheduled time is read as UTC.
    /// </summary>
    public static bool TryParse(long id, string? queue, string? scheduledAt, [NotNullWhen(true)] out JobKey? key)
    {
        key = null;

        if (string.IsNullOrEmpty(queue) || string.IsNullOrEmpty(scheduledAt)) return false;

        if (!DateTime.TryParse(scheduledAt, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var time))
        {
            return false;
        }

        key = new JobKey(queue, time, id);
        return true;
    }

    /// <summary>
    /// UTC in the round-trip format, which keeps every digit the database stores. The stored times are UTC, but the providers
    /// other than Npgsql return them without a kind.
    /// </summary>
    public static string FormatTime(DateTime time) => DateTime.SpecifyKind(time, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture);
}
