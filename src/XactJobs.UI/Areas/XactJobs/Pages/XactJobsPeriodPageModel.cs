using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using XactJobs.UI.Services;

namespace XactJobs.UI.Pages;

/// <summary>
/// A page of a list in a period of time, read a page at a time from the last row seen: the history, and the finished batches. The
/// period is worked out here at each request: the last 24 hours (the default), or today, yesterday, this week or a day in the
/// browser's time zone (<c>tz</c>, sent by the filters' form), so that a link to "today" still means today the next day.
/// </summary>
public abstract class XactJobsPeriodPageModel : XactJobsPageModel
{
    /// <summary>The longest period a page shows, as the API allows.</summary>
    public static readonly TimeSpan MaxPeriod = TimeSpan.FromDays(31);

    protected XactJobsPeriodPageModel(IXactJobsUIService service, XactJobsUIOptions options)
        : base(service, options)
    {
    }

    public static IReadOnlyList<(string Value, string Title)> Periods { get; } =
    [
        (LastDay, "Last 24 hours"),
        ("today", "Today"),
        ("yesterday", "Yesterday"),
        ("week", "This week"),
        ("day", "Day"),
    ];

    private const string LastDay = "24h";

    [BindProperty(SupportsGet = true)]
    public string? Period { get; set; }

    /// <summary>The day of the "day" period, yyyy-MM-dd, for the date field.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Day { get; set; }

    /// <summary>The browser's time zone (an IANA id), in which the days of the periods are; UTC if not set or not known.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Tz { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Cursor { get; set; }

    /// <summary>Whether the page shows the period filter (the batches only do for the finished ones).</summary>
    public virtual bool HasPeriodFilter => true;

    /// <summary>Whether the table is read from the last row seen, with <see cref="Cursor"/>, instead of by page number.</summary>
    public virtual bool UsesCursor => true;

    public string CurrentPeriod => Periods.Any(p => p.Value == Period) ? Period! : LastDay;

    public override string TableUrl => UsesCursor ? "?handler=Table" + CursorQuery(Cursor) : base.TableUrl;

    /// <summary>The query of the page of the table after the row of the cursor (null: the newest), relative to the current URL.</summary>
    public string CursorQuery(string? cursor) => QueryPart("cursor", cursor) + FilterQuery;

    /// <summary>
    /// The period's UTC times, from its local days in <see cref="Tz"/>, or the last 24 hours (also for a day that isn't valid).
    /// No end: until now, and the rows that arrive later.
    /// </summary>
    protected (DateTime From, DateTime? To) GetPeriod()
    {
        var now = DateTime.UtcNow;
        var zone = FindZone(Tz);
        var today = TimeZoneInfo.ConvertTimeFromUtc(now, zone).Date;

        DateTime? start = CurrentPeriod switch
        {
            "today" => today,
            "yesterday" => today.AddDays(-1),
            "week" => today.AddDays(-(((int)today.DayOfWeek + 6) % 7)), // from Monday
            "day" => DateTime.TryParseExact(Day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : null,
            _ => null
        };

        if (start == null) return (now.AddHours(-24), null);

        var end = start.Value.AddDays(CurrentPeriod == "week" ? 7 : 1);

        return (ToUtc(start.Value, zone), ToUtc(end, zone));
    }

    private static TimeZoneInfo FindZone(string? id)
    {
        return !string.IsNullOrEmpty(id) && id.Length <= 64 && TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone)
            ? zone
            : TimeZoneInfo.Utc;
    }

    /// <summary>A local midnight in UTC: in a zone whose clocks jump over midnight, the day starts when the gap ends.</summary>
    private static DateTime ToUtc(DateTime local, TimeZoneInfo zone)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);

        while (zone.IsInvalidTime(local))
        {
            local = local.AddMinutes(15);
        }

        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }
}
