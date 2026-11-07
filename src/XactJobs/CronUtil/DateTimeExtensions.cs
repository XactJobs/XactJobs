// Copyright (c) 2025 XactJobs
// Licensed under the MIT License. See LICENSE in the repository root.

namespace XactJobs.CronUtil
{
    internal static class DateTimeExtensions
    {
        public static DateTime Add(this DateTime dateTime, FieldType field, int value)
        {
			var result = dateTime;

            switch (field)
            {
                case FieldType.Seconds:
                    result = result.AddSeconds(value);
                    break;
                case FieldType.Minutes:
                    result = result.AddMinutes(value);
                    break;
                case FieldType.Hours:
                    result = result.AddHours(value);
                    break;
                case FieldType.DaysOfWeek:
                case FieldType.DaysOfMonth:
                    result = result.AddDays(value);
                    break;
                case FieldType.Months:
                    result = result.AddMonths(value);
                    break;
                case FieldType.Years:
                    result = result.AddYears(value);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(field));
            }

            return result;
        }

        public static DateTime SetField(this DateTime dateTime, FieldType field, int value)
        {
			var result = dateTime;

            switch (field)
            {
                case FieldType.Seconds:
                    result = result.AddSeconds(-result.Second + value);
                    break;
                case FieldType.Minutes:
                    result = result.AddMinutes(-result.Minute + value);
                    break;
                case FieldType.Hours:
                    result = result.AddHours(-result.Hour + value);
                    break;
                case FieldType.DaysOfWeek:
                    result = result.AddDays(-(int)result.DayOfWeek + value);
                    break;
                case FieldType.DaysOfMonth:
                    result = result.AddDays(-result.Day + value);
                    break;
                case FieldType.Months:
                    result = result.AddMonths(-result.Month + value);
                    break;
                case FieldType.Years:
                    result = result.AddYears(-result.Year + value);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(field));
            }

            return result;
        }

        public static DateTime Reset(this DateTime dateTime, FieldType field) 
		{
            return dateTime.SetField(field, field == FieldType.DaysOfMonth || field == FieldType.Months ? 1 : 0);
        }

        public static DateTime Reset(this DateTime dateTime, IReadOnlyList<FieldType> fieldTypes) 
		{
			var result = dateTime;

            foreach (var field in fieldTypes)
            {
                result = result.Reset(field);
            }

            return result;
        }

        /// <summary>
        /// Drops the fraction of a second: the milliseconds, and the ticks below them, so the times that follow are whole seconds.
        /// </summary>
        public static DateTime TruncateToSeconds(this DateTime dateTime)
        {
            return new DateTime(dateTime.Ticks - dateTime.Ticks % TimeSpan.TicksPerSecond, dateTime.Kind);
        }
    }
}
