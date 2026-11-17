// Copyright (c) 2025 XactJobs
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Collections.Concurrent;
using System.Globalization;

namespace XactJobs.CronUtil
{
    /// <summary>
    /// The local times a periodic job doesn't run at: a comma-separated list of dates (<c>12-25</c> every year, <c>2026-04-06</c> once)
    /// and daily time ranges (<c>12:00-13:00</c>, from the first time, to the second one, which isn't excluded; <c>22:00-06:00</c> crosses
    /// midnight). See <see cref="XactCronSchedule.Exclusions"/>.
    /// </summary>
    internal sealed class CronExclusions
    {
        /// <summary>The longest list, in characters: the most Oracle's NVARCHAR2 holds, as for the periodic state.</summary>
        public const int MaxLength = 2000;

        /// <summary>The lists parsed, by their text as given: a periodic run's is checked when it's claimed.</summary>
        private static readonly ConcurrentDictionary<string, CronExclusions> _parsed = new();

        private static readonly CronExclusions None = new([]);

        private readonly HashSet<(int Month, int Day)> _annualDates = [];
        private readonly HashSet<DateOnly> _dates = [];
        private readonly List<(TimeOnly From, TimeOnly To)> _timeRanges = [];

        /// <summary>The list as it's stored: its items trimmed and separated by a comma and a space, or null for none.</summary>
        public string? Text { get; }

        private CronExclusions(string[] items)
        {
            Text = items.Length == 0 ? null : string.Join(", ", items);

            if (Text?.Length > MaxLength)
            {
                throw new ArgumentException($"The exclusions are longer than {MaxLength} characters ({Text.Length}).");
            }

            foreach (var item in items)
            {
                if (!TryAdd(item))
                {
                    throw new ArgumentException($"Invalid exclusion '{item}': a date (MM-dd every year, yyyy-MM-dd once) or a time range (HH:mm-HH:mm).");
                }
            }
        }

        /// <summary>
        /// The list parsed (cached), or null if it's null: an empty one excludes nothing. Throws <see cref="ArgumentException"/> for an
        /// invalid one.
        /// </summary>
        public static CronExclusions? Parse(string? exclusions)
        {
            return exclusions == null ? null : _parsed.GetOrAdd(exclusions, static e => Split(e) is { Length: > 0 } items ? new CronExclusions(items) : None);
        }

        /// <summary>
        /// The list as it's stored, checked: its items trimmed and separated by a comma and a space, or null for none. So two lists that
        /// differ only in spaces are the same.
        /// </summary>
        public static string? Normalize(string? exclusions) => Parse(exclusions)?.Text;

        /// <summary>
        /// The first local time after the exclusion that <paramref name="local"/> is in, where the search for the next time goes on, or
        /// null if it isn't excluded: the next midnight for an excluded date, the end of a time range.
        /// </summary>
        public DateTime? ResumeAfter(DateTime local)
        {
            var date = DateOnly.FromDateTime(local);

            if (_dates.Contains(date) || _annualDates.Contains((date.Month, date.Day)))
            {
                return local.Date.AddDays(1);
            }

            var time = TimeOnly.FromDateTime(local);

            foreach (var (from, to) in _timeRanges)
            {
                if (from < to)
                {
                    if (time >= from && time < to) return local.Date + to.ToTimeSpan();
                }
                else if (time >= from)
                {
                    // crosses midnight: from tonight to tomorrow
                    return local.Date.AddDays(1) + to.ToTimeSpan();
                }
                else if (time < to)
                {
                    return local.Date + to.ToTimeSpan();
                }
            }

            return null;
        }

        private bool TryAdd(string item)
        {
            if (item.Contains(':'))
            {
                var dash = item.IndexOf('-');

                if (dash < 0
                    || !TimeOnly.TryParseExact(item[..dash], "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var from)
                    || !TimeOnly.TryParseExact(item[(dash + 1)..], "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var to)
                    || from == to)
                {
                    return false;
                }

                _timeRanges.Add((from, to));

                return true;
            }

            if (DateOnly.TryParseExact(item, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                _dates.Add(date);

                return true;
            }

            // MM-dd in a leap year, so that 02-29 is one
            if (DateOnly.TryParseExact("2000-" + item, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var annual) && item.Length == 5)
            {
                _annualDates.Add((annual.Month, annual.Day));

                return true;
            }

            return false;
        }

        private static string[] Split(string exclusions)
        {
            return exclusions.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        }
    }
}
