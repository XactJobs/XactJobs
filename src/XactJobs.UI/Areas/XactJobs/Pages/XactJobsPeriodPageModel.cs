using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using XactJobs.UI.Services;

namespace XactJobs.UI.Pages;

/// <summary>
/// A page of a list in a period of time, read a page at a time from the last row seen: the history, and the finished batches. The
/// browser works out the period in its own time zone (today, this week, ...) and sends it as UTC times (<c>from</c>, <c>to</c>); the
/// last 24 hours, the default, are worked out here at each request, so that the list moves with the time.
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

    [BindProperty(SupportsGet = true)]
    public string? From { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? To { get; set; }

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

    public override string FilterQuery => !HasPeriodFilter
        ? base.FilterQuery
        : base.FilterQuery
            + QueryPart("period", Period)
            + QueryPart("day", Day)
            + QueryPart("from", From)
            + QueryPart("to", To);

    /// <summary>
    /// The period's UTC times: the ones the browser sent, or the last 24 hours (also for times that aren't valid, or a period
    /// longer than <see cref="MaxPeriod"/>). No end: until now, and the rows that arrive later.
    /// </summary>
    protected (DateTime From, DateTime? To) GetPeriod()
    {
        var now = DateTime.UtcNow;

        if (CurrentPeriod != LastDay && TryParseUtc(From, out var from))
        {
            DateTime? to = TryParseUtc(To, out var end) ? end : null;

            if ((to ?? now) > from && (to ?? now) - from <= MaxPeriod) return (from, to);
        }

        return (now.AddHours(-24), null);
    }

    private static bool TryParseUtc(string? text, out DateTime value)
    {
        return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out value);
    }
}
