// This file is part of XactJobs.
//
// XactJobs is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as published
// by the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// XactJobs is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU Lesser General Public License for more details.
//
// You should have received a copy of the GNU Lesser General Public License
// along with this program. If not, see <https://www.gnu.org/licenses/>.

namespace XactJobs.Cron
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

        public static DateTime TruncateMs(this DateTime dateTime)
        {
            return dateTime.AddMilliseconds(-dateTime.Millisecond);
        }
    }
}
