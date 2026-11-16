// Copyright (c) 2025 XactJobs
// Licensed under the MIT License. See LICENSE in the repository root.

using XactJobs.CronUtil;

namespace XactJobs
{
    /// <summary>
    /// A periodic job's schedule: a cron expression, with seconds, in a time zone. A cron expression converts to a schedule in UTC,
    /// so it can be passed as it is: <c>Cron.Daily()</c>, or <c>new XactCronSchedule(Cron.Daily(9), "Europe/Zagreb")</c> for 9:00
    /// in Zagreb, also when it's summer time there.
    /// <para>
    /// Daylight saving time: when the clocks go forward, the times they skip run at the end of the gap, once (a job at 2:30 runs at
    /// 3:00 when 2:00 becomes 3:00). When they go back, a time that occurs twice runs once, the first time, unless the expression has
    /// an interval in its seconds, minutes or hours (<c>*</c>, a range or a step, e.g. every 15 minutes, or every hour): then it
    /// also runs the second time, so the interval goes on.
    /// </para>
    /// </summary>
    public readonly record struct XactCronSchedule
    {
        /// <summary>
        /// The cron expression: six fields, starting with the seconds.
        /// </summary>
        public string CronExpression { get; }

        /// <summary>
        /// The time zone's id, IANA (e.g. Europe/Zagreb), or null for UTC.
        /// </summary>
        public string? TimeZone { get; }

        /// <param name="cronExpression">Six fields, starting with the seconds, e.g. one of <see cref="Cron"/>'s.</param>
        /// <param name="timeZone">
        /// The time zone's id, or null for UTC. An IANA id (e.g. Europe/Zagreb), or a Windows one, which is stored as its IANA id when
        /// the system can convert it. Every node that runs the jobs must know it (on Linux, the tzdata package).
        /// </param>
        /// <exception cref="TimeZoneNotFoundException">The system doesn't know the time zone.</exception>
        public XactCronSchedule(string cronExpression, string? timeZone = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(cronExpression);

            CronExpression = cronExpression;
            TimeZone = CronTimeZones.Normalize(timeZone);
        }

        /// <param name="cronExpression">Six fields, starting with the seconds, e.g. one of <see cref="Cron"/>'s.</param>
        /// <param name="timeZone">The time zone. It's stored by its id: every node that runs the jobs must know it.</param>
        /// <exception cref="TimeZoneNotFoundException">The system can't find the time zone by its id.</exception>
        public XactCronSchedule(string cronExpression, TimeZoneInfo timeZone)
            : this(cronExpression, (timeZone ?? throw new ArgumentNullException(nameof(timeZone))).Id)
        {
        }

        /// <summary>
        /// The cron expression in UTC.
        /// </summary>
        public static implicit operator XactCronSchedule(string cronExpression) => new(cronExpression);

        public override string ToString() => TimeZone == null ? CronExpression : $"{CronExpression} ({TimeZone})";
    }
}
