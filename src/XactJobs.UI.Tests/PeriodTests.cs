using XactJobs.UI.Pages;

namespace XactJobs.UI.Tests;

public class PeriodTests
{
    private sealed class PeriodPage : XactJobsPeriodPageModel
    {
        public PeriodPage() : base(null!, new XactJobsUIOptions())
        {
        }

        public (DateTime From, DateTime? To) Get() => GetPeriod();

        protected override Task LoadAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private static (DateTime From, DateTime? To) Period(string period, string? day, string? tz) =>
        new PeriodPage { Period = period, Day = day, Tz = tz }.Get();

    private static DateTime Utc(string text) => DateTime.Parse(text, null, System.Globalization.DateTimeStyles.AdjustToUniversal);

    [Fact]
    public void Day_IsFromMidnightToMidnight_InTheZone_AlsoOnADaylightSavingChange()
    {
        // Zagreb springs forward on 30 March 2025: a day of 23 hours
        Assert.Equal((Utc("2025-03-29T23:00:00Z"), Utc("2025-03-30T22:00:00Z")), Period("day", "2025-03-30", "Europe/Zagreb"));
    }

    [Fact]
    public void Day_InAZoneWhoseClocksJumpOverMidnight_StartsWhenTheGapEnds()
    {
        // Santiago springs forward at midnight on 7 September 2025: the day starts at 01:00 (-03)
        Assert.Equal((Utc("2025-09-07T04:00:00Z"), Utc("2025-09-08T03:00:00Z")), Period("day", "2025-09-07", "America/Santiago"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Not/AZone")]
    public void Day_WithoutAKnownZone_IsInUtc(string? tz)
    {
        Assert.Equal((Utc("2025-03-30T00:00:00Z"), Utc("2025-03-31T00:00:00Z")), Period("day", "2025-03-30", tz));
    }

    [Fact]
    public void RelativePeriods_AreWorkedOutFromNow()
    {
        var today = DateTime.UtcNow.Date;
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));

        Assert.Equal((today, today.AddDays(1)), Period("today", null, "UTC"));
        Assert.Equal((today.AddDays(-1), today), Period("yesterday", null, "UTC"));
        Assert.Equal((monday, monday.AddDays(7)), Period("week", null, "UTC"));
    }

    [Theory]
    [InlineData("24h", null)]
    [InlineData("unknown", null)]
    [InlineData("day", null)]
    [InlineData("day", "not-a-day")]
    public void LastDay_IsTheDefault_AlsoForADayThatIsntValid(string period, string? day)
    {
        var (from, to) = Period(period, day, "Europe/Zagreb");

        Assert.InRange(from, DateTime.UtcNow.AddHours(-24).AddMinutes(-1), DateTime.UtcNow.AddHours(-24));
        Assert.Null(to);
    }
}
