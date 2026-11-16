using System.Globalization;
using XactJobs.CronUtil;

namespace XactJobs.Tests;

/// <summary>
/// The next times of cron expressions, in UTC and in time zones, around the changes of daylight saving time. Past dates (2025), so a
/// new tzdata doesn't change them. Like Hangfire (Cronos) and Quartz.NET 4: the local times the clocks skip run at the end of the gap, once; a local
/// time that occurs twice runs the first time, and also the second time if the expression has an interval in its seconds, minutes
/// or hours.
/// </summary>
public sealed class CronSequenceGeneratorTests
{
    // Europe/Zagreb: 2025-03-30 02:00 CET (+1) becomes 03:00 CEST (+2), at 01:00Z; 2025-10-26 03:00 CEST becomes 02:00 CET, at 01:00Z
    private const string Zagreb = "Europe/Zagreb";

    // America/New_York: 2025-03-09 02:00 EST (-5) becomes 03:00 EDT (-4), at 07:00Z; 2025-11-02 02:00 EDT becomes 01:00 EST, at 06:00Z
    private const string NewYork = "America/New_York";

    // Australia/Sydney: 2025-10-05 02:00 AEST (+10) becomes 03:00 AEDT (+11), at 2025-10-04 16:00Z
    private const string Sydney = "Australia/Sydney";

    // Australia/Lord_Howe, half an hour: 2025-10-05 02:00 (+10:30) becomes 02:30 (+11), at 2025-10-04 15:30Z;
    // 2025-04-06 02:00 (+11) becomes 01:30 (+10:30), at 2025-04-05 15:00Z
    private const string LordHowe = "Australia/Lord_Howe";

    [Theory]
    [InlineData("0 0 0 * * *", "2025-01-01T10:00:00Z", "2025-01-02T00:00:00Z")]
    [InlineData("*/15 * * * * *", "2025-01-01T10:00:00Z", "2025-01-01T10:00:15Z")]
    [InlineData("*/15 * * * * *", "2025-01-01T10:00:00.5Z", "2025-01-01T10:00:15Z")]
    [InlineData("*/15 * * * * *", "2025-01-01T09:59:59.9Z", "2025-01-01T10:00:00Z")]
    [InlineData("0 30 9 * * MON", "2025-01-01T10:00:00Z", "2025-01-06T09:30:00Z")]
    [InlineData("0 0 0 29 2 *", "2025-01-01T00:00:00Z", "2028-02-29T00:00:00Z")]
    public void Utc_GivesTheNextTimeAfter(string expression, string from, string expected)
    {
        Assert.Equal(Utc(expected), Next(expression, TimeZoneInfo.Utc, from));
    }

    [Theory]
    [InlineData("2025-01-15T12:00:00Z", "2025-01-16T08:00:00Z")] // CET, +1
    [InlineData("2025-07-15T12:00:00Z", "2025-07-16T07:00:00Z")] // CEST, +2
    public void TimeZone_RunsAtTheLocalTime(string from, string expected)
    {
        Assert.Equal(Utc(expected), Next("0 0 9 * * *", Zone(Zagreb), from));
    }

    [Fact]
    public void TimeZone_MatchesTheDayOfTheLocalTime()
    {
        // 23:30 CET on Sunday is 22:30Z: a job on Mondays at 00:00 local runs half an hour later, still on Sunday in UTC
        Assert.Equal(Utc("2025-01-05T23:00:00Z"), Next("0 0 0 * * MON", Zone(Zagreb), "2025-01-05T22:30:00Z"));
    }

    [Fact]
    public void Gap_TimeTheClocksSkip_RunsAtTheEndOfTheGap_Once()
    {
        var zone = Zone(Zagreb);

        // 02:30 doesn't exist on 2025-03-30: it runs at 03:00 CEST, then at 02:30 CEST the next day
        Assert.Equal(
            [Utc("2025-03-29T01:30:00Z"), Utc("2025-03-30T01:00:00Z"), Utc("2025-03-31T00:30:00Z")],
            Sequence("0 30 2 * * *", zone, "2025-03-28T12:00:00Z", 3));
    }

    [Fact]
    public void Gap_IntervalTimesTheClocksSkip_RunOnceAtTheEndOfTheGap()
    {
        // 01:45 CET, then 02:00, 02:15, 02:30, 02:45 and 03:00 are all 03:00 CEST, once, then 03:15 CEST
        Assert.Equal(
            [Utc("2025-03-30T00:45:00Z"), Utc("2025-03-30T01:00:00Z"), Utc("2025-03-30T01:15:00Z")],
            Sequence("0 */15 * * * *", Zone(Zagreb), "2025-03-30T00:30:00Z", 3));
    }

    [Theory]
    [InlineData(NewYork, "0 30 2 * * *", "2025-03-08T12:00:00Z", "2025-03-09T07:00:00Z")]
    [InlineData(Sydney, "0 30 2 * * *", "2025-10-04T00:00:00Z", "2025-10-04T16:00:00Z")]
    [InlineData(LordHowe, "0 15 2 * * *", "2025-10-04T00:00:00Z", "2025-10-04T15:30:00Z")]
    public void Gap_InOtherTimeZones_RunsAtTheEndOfTheGap(string zone, string expression, string from, string expected)
    {
        Assert.Equal(Utc(expected), Next(expression, Zone(zone), from));
    }

    [Fact]
    public void Overlap_FixedTime_RunsTheFirstTimeOnly()
    {
        // 02:30 occurs twice on 2025-10-26: at 02:30 CEST (00:30Z) and at 02:30 CET (01:30Z)
        Assert.Equal(
            [Utc("2025-10-26T00:30:00Z"), Utc("2025-10-27T01:30:00Z")],
            Sequence("0 30 2 * * *", Zone(Zagreb), "2025-10-25T12:00:00Z", 2));
    }

    [Theory]
    [InlineData("2025-10-26T01:10:00Z")] // 02:10 CET, the second time through the overlap, before 02:30
    [InlineData("2025-10-26T00:50:00Z")] // 02:50 CEST, the first time through, after 02:30
    public void Overlap_FixedTime_FromWithinTheOverlap_SkipsTheSecondTime(string from)
    {
        Assert.Equal(Utc("2025-10-27T01:30:00Z"), Next("0 30 2 * * *", Zone(Zagreb), from));
    }

    [Fact]
    public void Overlap_Interval_RunsBothTimes()
    {
        // 02:00 and 02:30 CEST, then 02:00 and 02:30 CET, then 03:00 CET: every 30 minutes
        Assert.Equal(
            [
                Utc("2025-10-26T00:00:00Z"), Utc("2025-10-26T00:30:00Z"), Utc("2025-10-26T01:00:00Z"),
                Utc("2025-10-26T01:30:00Z"), Utc("2025-10-26T02:00:00Z"),
            ],
            Sequence("0 */30 * * * *", Zone(Zagreb), "2025-10-25T23:45:00Z", 5));
    }

    [Fact]
    public void Overlap_Interval_FromWithinTheSecondTime_GoesOn()
    {
        // 02:10 CET: the next is 02:30 CET
        Assert.Equal(Utc("2025-10-26T01:30:00Z"), Next("0 */30 * * * *", Zone(Zagreb), "2025-10-26T01:10:00Z"));
    }

    [Theory]
    [InlineData("0 0 1-3 * * *", true)] // a range of hours
    [InlineData("0 0 1-2,4 * * *", true)] // a range in a list
    [InlineData("0 0 2,4 * * *", false)] // a list of fixed hours
    public void Overlap_RangeOfHours_IsAnInterval(string expression, bool runsTwice)
    {
        var runs = Sequence(expression, Zone(Zagreb), "2025-10-25T23:30:00Z", 2);

        // 02:00 CEST, then 02:00 CET if it's an interval, otherwise 03:00 or 04:00 CET
        Assert.Equal(Utc("2025-10-26T00:00:00Z"), runs[0]);
        Assert.Equal(runsTwice, runs[1] == Utc("2025-10-26T01:00:00Z"));
    }

    [Fact]
    public void Overlap_InNewYork_FixedTime_RunsTheFirstTimeOnly()
    {
        // 01:30 EDT (05:30Z), not 01:30 EST (06:30Z)
        Assert.Equal(
            [Utc("2025-11-02T05:30:00Z"), Utc("2025-11-03T06:30:00Z")],
            Sequence("0 30 1 * * *", Zone(NewYork), "2025-11-01T12:00:00Z", 2));
    }

    /// <summary>
    /// An interval stays an interval in real time across both changes, also by half an hour: every minute is 60 seconds after
    /// the one before, and every hour an hour after the one before.
    /// </summary>
    [Theory]
    [InlineData(Zagreb, "0 * * * * *", 60, "2025-03-29T22:00:00Z")]
    [InlineData(Zagreb, "0 * * * * *", 60, "2025-10-25T22:00:00Z")]
    [InlineData(Zagreb, "0 0 * * * *", 3600, "2025-03-29T12:00:00Z")]
    [InlineData(Zagreb, "0 0 * * * *", 3600, "2025-10-25T12:00:00Z")]
    [InlineData(NewYork, "0 * * * * *", 60, "2025-11-02T03:00:00Z")]
    [InlineData(LordHowe, "0 * * * * *", 60, "2025-10-04T12:00:00Z")]
    [InlineData(LordHowe, "0 * * * * *", 60, "2025-04-05T12:00:00Z")]
    public void Interval_KeepsItsLengthInRealTime_AcrossTheChanges(string zone, string expression, int seconds, string from)
    {
        var runs = Sequence(expression, Zone(zone), from, 24 * 3600 / seconds);

        Assert.All(runs.Zip(runs.Skip(1)), pair => Assert.Equal(TimeSpan.FromSeconds(seconds), pair.Second - pair.First));
    }

    [Fact]
    public void NextUtc_ReturnsUtc_AndRefusesOtherKinds()
    {
        var generator = new CronSequenceGenerator("0 0 0 * * *", Zone(Zagreb));

        Assert.Equal(DateTimeKind.Utc, generator.NextUtc(DateTime.UtcNow).Kind);
        Assert.Throws<ArgumentException>(() => generator.NextUtc(DateTime.Now));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("UTC", null)]
    [InlineData("Etc/UTC", null)]
    [InlineData(Zagreb, Zagreb)]
    public void Schedule_StoresTheTimeZone_AndNullForUtc(string? timeZone, string? expected)
    {
        Assert.Equal(expected, new XactCronSchedule(CronExpressions.Daily, timeZone).TimeZone);
    }

    [Fact]
    public void Schedule_StoresAWindowsId_AsItsIanaId()
    {
        // the system converts it to the zone of its default region
        Assert.Equal("Europe/Budapest", new XactCronSchedule(CronExpressions.Daily, "Central Europe Standard Time").TimeZone);
    }

    [Fact]
    public void Schedule_StoresATimeZoneInfo_ByItsId()
    {
        Assert.Equal(Zagreb, new XactCronSchedule(CronExpressions.Daily, Zone(Zagreb)).TimeZone);
        Assert.Null(new XactCronSchedule(CronExpressions.Daily, TimeZoneInfo.Utc).TimeZone);
    }

    [Fact]
    public void Schedule_RefusesAnUnknownTimeZone()
    {
        Assert.Throws<TimeZoneNotFoundException>(() => new XactCronSchedule(CronExpressions.Daily, "Nowhere/Atlantis"));
    }

    [Fact]
    public void Schedule_FromAString_IsInUtc()
    {
        XactCronSchedule schedule = CronExpressions.Hourly;

        Assert.Equal(new XactCronSchedule(CronExpressions.Hourly), schedule);
        Assert.Null(schedule.TimeZone);
        Assert.NotEqual(new XactCronSchedule(CronExpressions.Hourly, Zagreb), schedule);
    }

    private static DateTime Next(string expression, TimeZoneInfo zone, string from)
    {
        return new CronSequenceGenerator(expression, zone).NextUtc(Utc(from));
    }

    private static List<DateTime> Sequence(string expression, TimeZoneInfo zone, string from, int count)
    {
        var generator = new CronSequenceGenerator(expression, zone);
        var runs = new List<DateTime>();
        var time = Utc(from);

        for (var i = 0; i < count; i++)
        {
            time = generator.NextUtc(time);
            runs.Add(time);
        }

        return runs;
    }

    private static DateTime Utc(string time)
    {
        return DateTime.Parse(time, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
    }

    private static TimeZoneInfo Zone(string id) => TimeZoneInfo.FindSystemTimeZoneById(id);
}
