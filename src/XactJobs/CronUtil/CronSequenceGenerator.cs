// Copyright (c) 2025 XactJobs
// Licensed under the MIT License. See LICENSE in the repository root.
//
// Modeled on the Spring Framework's CronSequenceGenerator, copyright the original author or authors, licensed under the
// Apache License, Version 2.0 (see LICENSE-ASP.NET.txt for the license text, and http://www.apache.org/licenses/LICENSE-2.0).

using System.Collections;
using System.Globalization;

namespace XactJobs.CronUtil
{
    /// <summary>
    /// Modeled by Java Spring Frameworks's CronSequenceGenerator
	/// https://github.com/spring-projects/spring-framework/blob/master/spring-context/src/main/java/org/springframework/scheduling/support/CronSequenceGenerator.java
    /// </summary>
    class CronSequenceGenerator
	{
		public string Expression { get; }

        private readonly TimeZoneInfo _timeZoneInfo;
		private readonly CronExclusions? _exclusions;
        private readonly BitArray _months = new BitArray(13); // zero index is not used, that's why 13 and not 12
		private readonly BitArray _daysOfMonth = new BitArray(32); // zero index is not used
		private readonly BitArray _daysOfWeek = new BitArray(8); // seven can be sunday
		private readonly BitArray _hours = new BitArray(24);
		private readonly BitArray _minutes = new BitArray(60);
		private readonly BitArray _seconds = new BitArray(60);

		// the days that depend on the month, matched with a date (or with the bits above, in a list with them)
		private readonly List<int> _daysBeforeLastDayOfMonth = []; // L is 0, L-3 is 3
		private readonly List<int> _nearestWeekdays = []; // 15W: the weekday nearest the 15th, in its month
		private bool _isLastWeekdayOfMonth; // LW
		private readonly List<DayOfWeek> _lastDaysOfWeekOfMonth = []; // 5L, FRIL: the month's last Friday
		private readonly List<(DayOfWeek Day, int Nth)> _nthDaysOfWeekOfMonth = []; // 5#3, FRI#3: the month's third Friday

		// whether the day fields have any of the above: without them, a day is matched with the bits only
		private bool _hasDaysOfMonthOfTheMonth;
		private bool _hasDaysOfWeekOfTheMonth;

		private bool _isIntervalBased;

		public CronSequenceGenerator(string expression)
			: this(expression, TimeZoneInfo.Utc)
		{
		}

        public CronSequenceGenerator(string expression, TimeZoneInfo timeZoneInfo)
			: this(expression, timeZoneInfo, null)
        {
        }

		/// <param name="exclusions">The local times the sequence leaves out: see <see cref="CronExclusions"/>.</param>
        public CronSequenceGenerator(string expression, TimeZoneInfo timeZoneInfo, string? exclusions)
        {
			Expression = expression;
			_timeZoneInfo = timeZoneInfo;
			_exclusions = CronExclusions.Parse(exclusions);

			Parse();
        }

		/// <summary>
		/// The first time of the sequence after <paramref name="dateTimeUtc"/>, in whole seconds. The expression is matched with the
		/// local times of the time zone. When the clocks go forward, the local times they skip give the end of the gap (once, for all
		/// of them). When they go back, a local time that occurs twice gives the first one, and also the second one if the expression
		/// has an interval in its seconds, minutes or hours, so that the interval goes on. Like Hangfire (Cronos) and Quartz.NET 4.
		/// </summary>
		public DateTime NextUtc(DateTime dateTimeUtc)
		{
			if (dateTimeUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Only UTC times can be used", nameof(dateTimeUtc));

			var truncatedUtc = dateTimeUtc.TruncateToSeconds();

			var local = DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(truncatedUtc, _timeZoneInfo), DateTimeKind.Unspecified);

			if (!_timeZoneInfo.IsAmbiguousTime(local))
			{
				return ToUtc(FindNext(local.AddSeconds(1)));
			}

			// the clocks went back, or will go back soon: the local times between the start and the end of the overlap occur twice,
			// first with the daylight offset, then with the standard one
			var (daylightOffset, standardOffset) = GetAmbiguousOffsets(local);

			var transitionUtc = FindTransitionUtc(Utc(local - daylightOffset), Utc(local - standardOffset));

			var overlapStart = Local(transitionUtc + standardOffset);
			var overlapEnd = Local(transitionUtc + daylightOffset);

			var next = FindNext(local.AddSeconds(1));

			if (truncatedUtc < transitionUtc)
			{
				// the first time through the overlap
				if (next < overlapEnd) return Utc(next - daylightOffset);

				if (_isIntervalBased)
				{
					var second = FindNext(overlapStart);

					if (second < overlapEnd) return Utc(second - standardOffset);
				}

				return ToUtc(next);
			}

			// the second time through the overlap
			if (_isIntervalBased && next < overlapEnd) return Utc(next - standardOffset);

			return ToUtc(FindNext(overlapEnd));
		}

		/// <summary>
		/// The first local time of the sequence at or after <paramref name="local"/>.
		/// </summary>
		private DateTime FindNext(DateTime local)
		{
			var dot = local.Year;

			while (true)
			{
				var next = local;

				DoNext(ref next, dot);

				// an excluded time: the search goes on after its exclusion
				if (_exclusions?.ResumeAfter(next) is not { } resume) return next;

				ThrowIfRunaway(resume, dot);

				local = resume;
			}
		}

		/// <summary>
		/// The time of a local time of the sequence: the end of the gap for a time the clocks skip, the first one for a time that occurs
		/// twice (the second one is found by <see cref="NextUtc"/> when it's needed).
		/// </summary>
		private DateTime ToUtc(DateTime local)
		{
			if (_timeZoneInfo.IsInvalidTime(local))
			{
				// the transition is within 14 hours (the largest offset) of the local time
				var limit = TimeSpan.FromHours(15);

				return FindTransitionUtc(Utc(local - limit), Utc(local + limit));
			}

			if (_timeZoneInfo.IsAmbiguousTime(local))
			{
				return Utc(local - GetAmbiguousOffsets(local).Daylight);
			}

			return Utc(local - _timeZoneInfo.GetUtcOffset(local));
		}

		private (TimeSpan Daylight, TimeSpan Standard) GetAmbiguousOffsets(DateTime local)
		{
			var offsets = _timeZoneInfo.GetAmbiguousTimeOffsets(local);

			return (offsets.Max(), offsets.Min());
		}

		/// <summary>
		/// The first whole second after <paramref name="fromUtc"/> with the offset of <paramref name="toUtc"/>, which differs from the
		/// offset of <paramref name="fromUtc"/>: the time of the one transition between them.
		/// </summary>
		private DateTime FindTransitionUtc(DateTime fromUtc, DateTime toUtc)
		{
			var offset = _timeZoneInfo.GetUtcOffset(toUtc);

			if (_timeZoneInfo.GetUtcOffset(fromUtc) == offset)
			{
				throw new InvalidOperationException($"No change of the offset of time zone '{_timeZoneInfo.Id}' between {fromUtc:O} and {toUtc:O}");
			}

			var low = fromUtc.TruncateToSeconds();
			var high = toUtc.TruncateToSeconds();

			// the offset at low differs, the offset at high is the new one
			while (high - low > TimeSpan.FromSeconds(1))
			{
				var middle = low.AddSeconds(Math.Floor((high - low).TotalSeconds / 2));

				if (_timeZoneInfo.GetUtcOffset(middle) == offset)
				{
					high = middle;
				}
				else
				{
					low = middle;
				}
			}

			return high;
		}

		private static DateTime Utc(DateTime dateTime) => DateTime.SpecifyKind(dateTime, DateTimeKind.Utc);

		private static DateTime Local(DateTime dateTime) => DateTime.SpecifyKind(dateTime, DateTimeKind.Unspecified);

		/// <summary>
		/// Determine whether the specified expression represents a valid cron pattern.
		/// </summary>
		/// <param name="expression">the expression to evaluate</param>
		/// <returns>true if the given expression is a valid cron expression, otherwise false</returns>
		public static bool IsValidExpression(string expression) 
		{
			if (expression == null) 
			{
				return false;
			}

            try
            {
				new CronSequenceGenerator(expression);
				return true;
			}
			catch (ArgumentException)
            {
				return false;
			}
		}

		private void DoNext(ref DateTime dateTime, int dot)
		{
			var resets = new List<FieldType>();

			var second = dateTime.Second;
			var updateSecond = FindNext(ref dateTime, _seconds, second, FieldType.Seconds, FieldType.Minutes, new List<FieldType>());
			if (second == updateSecond)
			{
				resets.Add(FieldType.Seconds);
			}

			var minute = dateTime.Minute;
			var updateMinute = FindNext(ref dateTime, _minutes, minute, FieldType.Minutes, FieldType.Hours, resets);
			if (minute == updateMinute)
			{
				resets.Add(FieldType.Minutes);
			}
			else
			{
				DoNext(ref dateTime, dot);
			}

			var hour = dateTime.Hour;
			var updateHour = FindNext(ref dateTime, _hours, hour, FieldType.Hours, FieldType.DaysOfWeek, resets);
			if (hour == updateHour)
			{
				resets.Add(FieldType.Hours);
			}
			else
			{
				DoNext(ref dateTime, dot);
			}

			var dayOfMonth = dateTime.Day;
			var updateDayOfMonth = FindNextDay(ref dateTime, dot, resets);
			if (dayOfMonth == updateDayOfMonth)
			{
				resets.Add(FieldType.DaysOfMonth);
			}
			else
			{
				DoNext(ref dateTime, dot);
			}

			var month = dateTime.Month;
			var updateMonth = FindNext(ref dateTime, _months, month, FieldType.Months, FieldType.Years, resets);
			if (month != updateMonth)
			{
				ThrowIfRunaway(dateTime, dot);

				DoNext(ref dateTime, dot);
			}
		}

		private int FindNextDay(ref DateTime dateTime, int dot, IReadOnlyList<FieldType> resets)
		{
			while (!MatchesDay(dateTime))
			{
				dateTime = dateTime.Add(FieldType.DaysOfMonth, 1);

				dateTime = dateTime.Reset(resets);

				ThrowIfRunaway(dateTime, dot);
			}

			return dateTime.Day;
		}

		/// <summary>
		/// The years the search for the next time goes through, from the year it started in (<c>dot</c>), before it decides
		/// the expression has none: the days of a valid one repeat within 40 years (a Friday 29 February, FRI#5 with FEB: 2188, then
		/// 2228, since 2200 isn't a leap year), and days that depend on the month can be more than a year apart (L-30 with FRI).
		/// </summary>
		private const int MaxSearchYears = 50;

		private void ThrowIfRunaway(DateTime dateTime, int dot)
		{
			if (dateTime.Year - dot > MaxSearchYears)
			{
				throw new ArgumentException($"Invalid cron expression '{Expression}': no time matches it in {MaxSearchYears} years");
			}
		}

		/// <summary>Both day fields match the date: each one with its values, or with one of its days that depend on the month.</summary>
		private bool MatchesDay(DateTime date)
		{
			return (_daysOfMonth.Get(date.Day) || (_hasDaysOfMonthOfTheMonth && MatchesDayOfMonthOfTheMonth(date)))
				&& (_daysOfWeek.Get((int)date.DayOfWeek) || (_hasDaysOfWeekOfTheMonth && MatchesDayOfWeekOfTheMonth(date)));
		}

		private bool MatchesDayOfMonthOfTheMonth(DateTime date)
		{
			var daysInMonth = DateTime.DaysInMonth(date.Year, date.Month);

			foreach (var daysBefore in _daysBeforeLastDayOfMonth)
			{
				if (date.Day == daysInMonth - daysBefore) return true;
			}

			if (_isLastWeekdayOfMonth && date.Day == NearestWeekday(date.Year, date.Month, daysInMonth)) return true;

			foreach (var day in _nearestWeekdays)
			{
				// a day the month doesn't have has no nearest weekday in it (31W in April)
				if (day <= daysInMonth && date.Day == NearestWeekday(date.Year, date.Month, day)) return true;
			}

			return false;
		}

		private bool MatchesDayOfWeekOfTheMonth(DateTime date)
		{
			if (_lastDaysOfWeekOfMonth.Count > 0)
			{
				var isLastWeekOfMonth = date.Day + 7 > DateTime.DaysInMonth(date.Year, date.Month);

				foreach (var dayOfWeek in _lastDaysOfWeekOfMonth)
				{
					if (date.DayOfWeek == dayOfWeek && isLastWeekOfMonth) return true;
				}
			}

			foreach (var (dayOfWeek, nth) in _nthDaysOfWeekOfMonth)
			{
				if (date.DayOfWeek == dayOfWeek && (date.Day - 1) / 7 + 1 == nth) return true;
			}

			return false;
		}

		/// <summary>
		/// The weekday (Monday to Friday) nearest the day, in its month, like Quartz's W: a Saturday gives the Friday before, a Sunday the
		/// Monday after, unless that's in another month, then the Monday after the Saturday 1st, or the Friday before the Sunday last day.
		/// </summary>
		private static int NearestWeekday(int year, int month, int day)
		{
			var daysInMonth = DateTime.DaysInMonth(year, month);

			return new DateTime(year, month, day).DayOfWeek switch
			{
				DayOfWeek.Saturday => day == 1 ? 3 : day - 1,
				DayOfWeek.Sunday => day == daysInMonth ? day - 2 : day + 1,
				_ => day,
			};
		}

		/// <summary>
		/// Search the bits provided for the next set bit after the value provided, and reset the dateTime.
		/// </summary>
		/// <param name="dateTime">Current date/time being modified</param>
		/// <param name="bits">a <see cref="BitArray"/> representing the allowed values of the field</param>
		/// <param name="value">the current value of the field</param>
		/// <param name="field">the field to increment in the dateTime. See <see cref="FieldType"/> for the valid field values).</param>
		/// <param name="nextField">next field to be incremented if roll-over happens (<see cref="FieldType"/>)</param>
		/// <param name="lowerOrders">fields that should be reset (i.e. the ones of lower significance than the field of interest)</param>
		/// <returns>The value of the field that is next in the sequence</returns>
		private int FindNext(ref DateTime dateTime, BitArray bits, int value, FieldType field, FieldType nextField, IReadOnlyList<FieldType> lowerOrders)
		{
			int nextValue = bits.GetNextSetBit(value);
			
			// roll over if needed
			if (nextValue == -1)
			{
				dateTime = dateTime
					.Add(nextField, 1)
					.Reset(field);

				nextValue = bits.GetNextSetBit(0);
			}

			if (nextValue != value)
			{
				dateTime = dateTime
					.SetField(field, nextValue)
					.Reset(lowerOrders);
			}

			return nextValue;
		}

        private void Parse()
		{
			var fields = Expression.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

			if (!AreValidCronFields(fields))
			{
				throw new ArgumentException($"Cron expression must consist of 6 fields (found {fields.Length} in '{Expression}')");
			}

			SetNumberHits(_seconds, fields[0], 0, 60, out var isIntervalSec);
			SetNumberHits(_minutes, fields[1], 0, 60, out var isIntervalMin);
			SetNumberHits(_hours, fields[2], 0, 24, out var isIntervalHr);
			SetDaysOfMonth(_daysOfMonth, ParseDaysOfMonthOfTheMonth(fields[3].ToUpperInvariant()));
			SetMonths(_months, fields[4]);
			SetDays(_daysOfWeek, ParseDaysOfWeekOfTheMonth(ReplaceOrdinals(fields[5], "SUN,MON,TUE,WED,THU,FRI,SAT")), 8);

			if (_daysOfWeek.Get(7)) {
				// Sunday can be represented as 0 or 7
				_daysOfWeek.Set(0, true);
				_daysOfWeek.Set(7, false);
			}

			_hasDaysOfMonthOfTheMonth = _daysBeforeLastDayOfMonth.Count > 0 || _nearestWeekdays.Count > 0 || _isLastWeekdayOfMonth;
			_hasDaysOfWeekOfTheMonth = _lastDaysOfWeekOfMonth.Count > 0 || _nthDaysOfWeekOfMonth.Count > 0;

			_isIntervalBased = isIntervalSec || isIntervalMin || isIntervalHr;
		}

		/// <summary>
		/// Takes L, L-n, LW and nW out of the day-of-month field's list, and gives the rest, which can be empty: then no bit is set.
		/// </summary>
		private string? ParseDaysOfMonthOfTheMonth(string field)
		{
			var rest = new List<string>();
			var items = field.Split(',');

			ThrowIfQuestionMarkInAList(items);

			foreach (var item in items)
			{
				if (item == "L")
				{
					_daysBeforeLastDayOfMonth.Add(0);
				}
				else if (item == "LW")
				{
					_isLastWeekdayOfMonth = true;
				}
				else if (item.StartsWith("L-", StringComparison.Ordinal))
				{
					if (!int.TryParse(item.AsSpan(2), NumberStyles.None, CultureInfo.InvariantCulture, out var daysBefore) || daysBefore < 0 || daysBefore > 30)
					{
						throw new ArgumentException($"Invalid days before the last day of the month: '{item}' in expression '{Expression}' (L-0 to L-30)");
					}

					_daysBeforeLastDayOfMonth.Add(daysBefore);
				}
				else if (item.EndsWith('W'))
				{
					if (!int.TryParse(item.AsSpan(0, item.Length - 1), NumberStyles.None, CultureInfo.InvariantCulture, out var day) || day < 1 || day > 31)
					{
						throw new ArgumentException($"Invalid nearest weekday: '{item}' in expression '{Expression}' (1W to 31W, or LW)");
					}

					_nearestWeekdays.Add(day);
				}
				else if (item.Contains('L') || item.Contains('W') || item.Contains('#'))
				{
					throw new ArgumentException($"Invalid day of month: '{item}' in expression '{Expression}'");
				}
				else
				{
					rest.Add(item);
				}
			}

			return rest.Count > 0 ? string.Join(',', rest) : null;
		}

		/// <summary>
		/// Takes nL and n#k out of the day-of-week field's list (names already replaced by numbers), and gives the rest, which can be
		/// empty: then no bit is set.
		/// </summary>
		private string? ParseDaysOfWeekOfTheMonth(string field)
		{
			var rest = new List<string>();
			var items = field.Split(',');

			ThrowIfQuestionMarkInAList(items);

			foreach (var item in items)
			{
				var hash = item.IndexOf('#');

				if (hash >= 0)
				{
					if (!TryParseDayOfWeek(item[..hash], out var dayOfWeek)
						|| !int.TryParse(item.AsSpan(hash + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var nth) || nth < 1 || nth > 5)
					{
						throw new ArgumentException($"Invalid n-th day of the week: '{item}' in expression '{Expression}' (e.g. FRI#3, the third Friday: 1 to 5)");
					}

					_nthDaysOfWeekOfMonth.Add((dayOfWeek, nth));
				}
				else if (item.EndsWith('L'))
				{
					if (!TryParseDayOfWeek(item[..^1], out var dayOfWeek))
					{
						throw new ArgumentException($"Invalid last day of the week: '{item}' in expression '{Expression}' (e.g. FRIL or 5L, the last Friday)");
					}

					_lastDaysOfWeekOfMonth.Add(dayOfWeek);
				}
				else if (item.Contains('L') || item.Contains('W'))
				{
					throw new ArgumentException($"Invalid day of week: '{item}' in expression '{Expression}'");
				}
				else
				{
					rest.Add(item);
				}
			}

			return rest.Count > 0 ? string.Join(',', rest) : null;
		}

		/// <summary>? is the whole field (any day): in a list with other values, it would make them meaningless.</summary>
		private void ThrowIfQuestionMarkInAList(string[] items)
		{
			if (items.Length > 1 && items.Any(i => i.Contains('?')))
			{
				throw new ArgumentException($"? can't be in a list: '{string.Join(',', items)}' in expression '{Expression}'");
			}
		}

		/// <summary>A day of the week, 0 to 7 (0 and 7 are Sunday).</summary>
		private static bool TryParseDayOfWeek(string value, out DayOfWeek dayOfWeek)
		{
			dayOfWeek = default;

			if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var day) || day > 7) return false;

			dayOfWeek = (DayOfWeek)(day % 7);

			return true;
		}

		private void SetDaysOfMonth(BitArray bits, string? field)
		{
			if (field == null) return;

			int max = 31;

			// Days of month start with 1 (in Cron and DateTime) so add one
			SetDays(bits, field, max + 1);

			// ... and remove it from the front
			bits.Set(0, false);
		}

		private void SetDays(BitArray bits, string? field, int max)
		{
			if (field == null) return;

			if (field.Contains("?"))
			{
				field = "*";
			}

			SetNumberHits(bits, field, 0, max, out _);
		}

		private void SetMonths(BitArray bits, string value)
		{
			int max = 12;
			value = ReplaceOrdinals(value, "FOO,JAN,FEB,MAR,APR,MAY,JUN,JUL,AUG,SEP,OCT,NOV,DEC");

			// Months start with 1 (in Cron and DateTime) so add one
			SetNumberHits(bits, value, 1, max + 1, out _);

            // ... and remove it from the front
			bits.Set(0, false);
		}

		private void SetNumberHits(BitArray bits, string value, int min, int max, out bool isInterval)
		{
			isInterval = false;

			var fields = value.Split(',');

			foreach (var field in fields)
			{
				if (!field.Contains("/"))
				{
					// Not an incrementer so it must be a range (possibly empty)
					var range = GetRange(field, min, max);
					for (var i = range[0]; i <= range[1]; i++)
					{
						bits.Set(i, true);
					}

					isInterval |= range[0] != range[1];
				}
				else
				{
					isInterval = true;

					var split = field.Split('/');
					if (split.Length != 2) throw new ArgumentException($"Incrementer has more than two fields: '{field}' in expression '{Expression}'");

					var range = GetRange(split[0], min, max);
					if (!split[0].Contains("-"))
					{
						range[1] = max - 1;
					}

					if (!int.TryParse(split[1], out var delta)) throw new ArgumentException($"Invalid incrementer delta in field '{field}' in expression '{Expression}'");
					if (delta <= 0) throw new ArgumentException($"Incrementer delta must be 1 or higher: '{field}' in expression '{Expression}'");

					for (int i = range[0]; i <= range[1]; i += delta)
					{
						bits.Set(i, true);
					}
				}
			}
		}

		private int[] GetRange(string field, int min, int max)
		{
			int[] result = new int[2];

			if (field.Contains("*"))
			{
				result[0] = min;
				result[1] = max - 1;
				return result;
			}

			if (!field.Contains("-"))
			{
				if (!int.TryParse(field, out var fieldValue)) throw new ArgumentException($"Invalid cron expression field '{field}'");
				result[0] = result[1] = fieldValue;
			}
			else
			{
				var split = field.Split('-');
				if (split.Length != 2) throw new ArgumentException($"Range '{field}' doesn't have two fields in expression '{Expression}'");
				if (!int.TryParse(split[0], out var fromValue)) throw new ArgumentException($"Invalid from value '{split[0]}' in range '{field}'");
				if (!int.TryParse(split[1], out var toValue)) throw new ArgumentException($"Invalid to value '{split[1]}' in range '{field}'");

				result[0] = fromValue;
				result[1] = toValue;
			}

			if (result[0] >= max || result[1] >= max) throw new ArgumentException($"Range exceeds maximum ({max}): '{field}' in expression '{Expression}'");
			if (result[0] < min || result[1] < min) throw new ArgumentException($"Range less than minimum ({min}): '{field}' in expression '{Expression}'");
			if (result[0] > result[1]) throw new ArgumentException($"Invalid inverted range: '{field}' in expression '{Expression}'");

			return result;
		}

		/// <summary>
		/// Replace the values in the comma-separated list (case insensitive) with their index in the list.
		/// </summary>
		/// <returns>a new String with the values from the list replaced</returns>
		private string ReplaceOrdinals(string value, string commaSeparatedList)
		{
			var list = commaSeparatedList.Split(',');
			for (int i = 0; i < list.Length; i++)
			{
				var item = list[i].ToUpperInvariant();
				value = value.ToUpperInvariant().Replace(item, "" + i);
			}
			return value;
		}

		private static bool AreValidCronFields(IReadOnlyList<string> fields) 
		{
			return fields != null && fields.Count == 6;
		}
    }
}
