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

        private readonly string? _exclusions;

        /// <summary>
        /// The local times the job doesn't run at, in the time zone, or null: a comma-separated list of dates, <c>MM-dd</c> every year
        /// (<c>12-25</c>) or <c>yyyy-MM-dd</c> once (<c>2026-04-06</c>), days of the week (<c>SAT</c>, <c>MON-FRI</c>), and daily time
        /// ranges, <c>HH:mm-HH:mm</c> (<c>12:00-13:00</c>, the end not included; <c>22:00-06:00</c> crosses midnight). E.g. a
        /// <c>const string</c> of holidays, shared by many jobs and attributes. At most 2000 characters.
        /// <para>
        /// An item can join one of each kind with <c>+</c>, and excludes the times that all of them match: <c>MON-FRI+00:00-05:00</c>,
        /// <c>01-15+MON-FRI</c>. Each can be a list in parentheses: <c>(MON,WED,FRI)+(00:00-01:00,06:00-07:00)</c>. A time range that
        /// crosses midnight belongs to the day it starts: <c>MON+22:00-01:00</c> is from Monday 22:00 to Tuesday 01:00.
        /// </para>
        /// <para>
        /// They belong to the periodic job, not to its definition: changing them doesn't make a new version, and the pending run is
        /// moved to the next time they allow (if it isn't running). A run whose time they exclude when it's due is skipped.
        /// </para>
        /// </summary>
        /// <exception cref="ArgumentException">An invalid item, a list longer than 2000 characters, or one that leaves no time.</exception>
        public string? Exclusions
        {
            get => _exclusions;
            init
            {
                _exclusions = CronExclusions.Normalize(value);

                // after the expression and the time zone, which the constructor sets
                if (_exclusions != null && CronExpression != null)
                {
                    try
                    {
                        new CronSequenceGenerator(CronExpression, CronTimeZones.Find(TimeZone), _exclusions).NextUtc(DateTime.UtcNow);
                    }
                    catch (ArgumentException ex)
                    {
                        throw new ArgumentException($"The exclusions '{_exclusions}' leave no time of '{CronExpression}' in 50 years, or the expression has none.", ex);
                    }
                }
            }
        }

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

        public override string ToString()
        {
            var schedule = TimeZone == null ? CronExpression : $"{CronExpression} ({TimeZone})";

            return Exclusions == null ? schedule : $"{schedule}, except {Exclusions}";
        }
    }
}
