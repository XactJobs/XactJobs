// Copyright (c) 2025 XactJobs
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Collections.Concurrent;
using System.Globalization;

namespace XactJobs.CronUtil
{
    /// <summary>
    /// The local times a periodic job doesn't run at: a comma-separated list of items, each excluding the times that all its parts match,
    /// joined with <c>+</c>: dates (<c>12-25</c> every year, <c>2026-04-06</c> once), days of the week (<c>SAT</c>, <c>MON-FRI</c>) and daily
    /// time ranges (<c>12:00-13:00</c>, from the first time, to the second one, which isn't excluded; <c>22:00-06:00</c> crosses midnight).
    /// A part can be a list in parentheses: <c>(MON,WED)+(00:00-01:00,06:00-07:00)</c>. A time range that crosses midnight belongs to the
    /// day it starts: <c>FRI+22:00-02:00</c> is from Friday 22:00 to Saturday 02:00. See <see cref="XactCronSchedule.Exclusions"/>.
    /// </summary>
    internal sealed class CronExclusions
    {
        /// <summary>The longest list, in characters: the most Oracle's NVARCHAR2 holds, as for the periodic state.</summary>
        public const int MaxLength = 2000;

        private const string Formats = "dates (MM-dd every year, yyyy-MM-dd once), days of the week (MON, MON-FRI) or time ranges (HH:mm-HH:mm), "
            + "joined with + (at most one of each kind), each kind a single value or a list in parentheses";

        private static readonly string[] DayNames = ["SUN", "MON", "TUE", "WED", "THU", "FRI", "SAT"];

        /// <summary>The lists parsed, by their text as given: a periodic run's is checked when it's claimed.</summary>
        private static readonly ConcurrentDictionary<string, CronExclusions> _parsed = new();

        private static readonly CronExclusions None = new([]);

        // Items with one part each are merged into one rule per kind, so that a long list of holidays stays one set
        private readonly Rule _dates = new() { Dates = new() };
        private readonly Rule _days = new() { Days = new bool[7] };
        private readonly Rule _times = new() { Times = [] };

        private readonly List<Rule> _rules = [];

        /// <summary>The list as it's stored: its items normalized and separated by a comma and a space, or null for none.</summary>
        public string? Text { get; }

        private CronExclusions(string[] items)
        {
            var normalized = new string[items.Length];

            for (var i = 0; i < items.Length; i++)
            {
                normalized[i] = Add(items[i])
                    ?? throw new ArgumentException($"Invalid exclusion '{items[i]}': {Formats}.");
            }

            Text = items.Length == 0 ? null : string.Join(", ", normalized);

            if (Text?.Length > MaxLength)
            {
                throw new ArgumentException($"The exclusions are longer than {MaxLength} characters ({Text.Length}).");
            }

            if (!_dates.Dates!.IsEmpty) _rules.Add(_dates);
            if (Array.IndexOf(_days.Days!, true) >= 0) _rules.Add(_days);
            if (_times.Times!.Count > 0) _rules.Add(_times);
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
        /// The list as it's stored, checked: its items normalized (no spaces in an item, the parts in the order dates, days, times, the
        /// days in capitals) and separated by a comma and a space, or null for none. So two lists written differently are the same.
        /// </summary>
        public static string? Normalize(string? exclusions) => Parse(exclusions)?.Text;

        /// <summary>
        /// The first local time after the exclusion that <paramref name="local"/> is in, where the search for the next time goes on, or
        /// null if it isn't excluded: the next midnight for an excluded day, the end of a time range.
        /// </summary>
        public DateTime? ResumeAfter(DateTime local)
        {
            foreach (var rule in _rules)
            {
                if (rule.ResumeAfter(local) is { } resume) return resume;
            }

            return null;
        }

        /// <summary>The item normalized, or null if it's invalid.</summary>
        private string? Add(string item)
        {
            var rule = new Rule();

            // the parts normalized
            string? dates = null, days = null, times = null;

            foreach (var part in item.Replace(" ", "").Split('+'))
            {
                var grouped = part.StartsWith('(') && part.EndsWith(')');
                var values = (grouped ? part[1..^1] : part).Split(',');

                if (values.Any(v => v.Length == 0 || v.Contains('(') || v.Contains(')'))) return null;

                // the kind of the first value: every value of the part must be of it
                if (values[0].Contains(':'))
                {
                    if (rule.Times != null) return null;

                    rule.Times = [];

                    if (!values.All(v => TryAddTime(rule.Times, v))) return null;

                    times = Format(values);
                }
                else if (char.IsLetter(values[0][0]))
                {
                    if (rule.Days != null) return null;

                    rule.Days = new bool[7];

                    for (var i = 0; i < values.Length; i++)
                    {
                        values[i] = values[i].ToUpperInvariant();

                        if (!TryAddDays(rule.Days, values[i])) return null;
                    }

                    days = Format(values);
                }
                else
                {
                    if (rule.Dates != null) return null;

                    rule.Dates = new();

                    if (!values.All(v => TryAddDate(rule.Dates, v))) return null;

                    dates = Format(values);
                }
            }

            // one kind: merged with the other items of its kind
            if ((rule.Dates != null ? 1 : 0) + (rule.Days != null ? 1 : 0) + (rule.Times != null ? 1 : 0) == 1)
            {
                rule.Dates?.CopyTo(_dates.Dates!);
                rule.Times?.ForEach(_times.Times!.Add);

                for (var i = 0; rule.Days != null && i < 7; i++)
                {
                    _days.Days![i] |= rule.Days[i];
                }
            }
            else
            {
                _rules.Add(rule);
            }

            return string.Join("+", new[] { dates, days, times }.Where(p => p != null));
        }

        private static bool TryAddTime(List<(TimeOnly From, TimeOnly To)> times, string value)
        {
            var dash = value.IndexOf('-');

            if (dash < 0
                || !TimeOnly.TryParseExact(value[..dash], "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var from)
                || !TimeOnly.TryParseExact(value[(dash + 1)..], "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var to)
                || from == to)
            {
                return false;
            }

            times.Add((from, to));

            return true;
        }

        private static bool TryAddDays(bool[] days, string value)
        {
            var dash = value.IndexOf('-');

            var first = Array.IndexOf(DayNames, dash < 0 ? value : value[..dash]);
            var last = dash < 0 ? first : Array.IndexOf(DayNames, value[(dash + 1)..]);

            if (first < 0 || last < 0) return false;

            // a range may go over the end of the week: FRI-MON
            for (var day = first; ; day = (day + 1) % 7)
            {
                days[day] = true;

                if (day == last) return true;
            }
        }

        private static bool TryAddDate(DateSet dates, string value)
        {
            if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                dates.Once.Add(date);

                return true;
            }

            // MM-dd in a leap year, so that 02-29 is one
            if (value.Length == 5 && DateOnly.TryParseExact("2000-" + value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var annual))
            {
                dates.Annual.Add((annual.Month, annual.Day));

                return true;
            }

            return false;
        }

        // A part as it's written in the normalized item: its values as given, in parentheses if there are several
        private static string Format(string[] values) => values.Length == 1 ? values[0] : $"({string.Join(",", values)})";

        /// <summary>
        /// The items in the list: split at the commas outside parentheses, trimmed, without empty ones. Throws for unbalanced parentheses.
        /// </summary>
        private static string[] Split(string exclusions)
        {
            var items = new List<string>();
            var depth = 0;
            var start = 0;

            for (var i = 0; i < exclusions.Length; i++)
            {
                switch (exclusions[i])
                {
                    case '(' when ++depth > 1:
                    case ')' when --depth < 0:
                        throw InvalidParentheses(exclusions);

                    case ',' when depth == 0:
                        items.Add(exclusions[start..i]);
                        start = i + 1;
                        break;
                }
            }

            if (depth > 0) throw InvalidParentheses(exclusions);

            items.Add(exclusions[start..]);

            return [.. items.Select(i => i.Trim()).Where(i => i.Length > 0)];
        }

        private static ArgumentException InvalidParentheses(string exclusions)
        {
            return new ArgumentException($"Invalid exclusions '{exclusions}': parentheses can't be nested, and each one must be closed.");
        }

        private sealed class DateSet
        {
            public readonly HashSet<(int Month, int Day)> Annual = [];
            public readonly HashSet<DateOnly> Once = [];

            public bool IsEmpty => Annual.Count == 0 && Once.Count == 0;

            public bool Contains(DateOnly date) => Once.Contains(date) || Annual.Contains((date.Month, date.Day));

            public void CopyTo(DateSet other)
            {
                other.Annual.UnionWith(Annual);
                other.Once.UnionWith(Once);
            }
        }

        /// <summary>
        /// One item: the times that all its parts match. A part that isn't set matches every time.
        /// </summary>
        private sealed class Rule
        {
            public DateSet? Dates;
            public bool[]? Days;
            public List<(TimeOnly From, TimeOnly To)>? Times;

            private bool MatchesDay(DateOnly date) => (Dates == null || Dates.Contains(date)) && (Days == null || Days[(int)date.DayOfWeek]);

            public DateTime? ResumeAfter(DateTime local)
            {
                var date = DateOnly.FromDateTime(local);

                if (Times == null) return MatchesDay(date) ? local.Date.AddDays(1) : null;

                var time = TimeOnly.FromDateTime(local);

                foreach (var (from, to) in Times)
                {
                    if (from < to)
                    {
                        if (time >= from && time < to && MatchesDay(date)) return local.Date + to.ToTimeSpan();
                    }
                    else if (time >= from)
                    {
                        // crosses midnight: from tonight to tomorrow, if it starts today
                        if (MatchesDay(date)) return local.Date.AddDays(1) + to.ToTimeSpan();
                    }
                    else if (time < to && MatchesDay(date.AddDays(-1)))
                    {
                        // the part after midnight of a range that started yesterday
                        return local.Date + to.ToTimeSpan();
                    }
                }

                return null;
            }
        }
    }
}
