namespace XactJobs.Cron
{
    public static class CronBuilder
    {
        /// <summary>
        /// Every X seconds (0-59)
        /// </summary>
        /// <param name="seconds"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public static string SecondInterval(int seconds)
        {
            if (seconds <= 0 || seconds > 59)
                throw new ArgumentOutOfRangeException(nameof(seconds), "Seconds must be between 1 and 59.");

            return $"*/{seconds} * * * * *";
        }

        /// <summary>
        /// Every X minutes (0-59)
        /// </summary>
        /// <param name="minutes"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public static string MinuteInterval(int minutes)
        {
            if (minutes <= 0 || minutes > 59)
                throw new ArgumentOutOfRangeException(nameof(minutes), "Minutes must be between 1 and 59.");

            return $"0 */{minutes} * * * *";
        }

        /// <summary>
        /// Every X hours (0-23)
        /// </summary>
        /// <param name="hours"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public static string HourInterval(int hours)
        {
            if (hours <= 0 || hours > 23)
                throw new ArgumentOutOfRangeException(nameof(hours), "Hours must be between 1 and 23.");

            return $"0 0 */{hours} * * *";
        }

        /// <summary>
        /// Every day at specific time (HH:mm:ss)
        /// </summary>
        /// <param name="time"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public static string Daily(TimeSpan time)
        {
            if (time.TotalHours >= 24)
                throw new ArgumentOutOfRangeException(nameof(time), "Time must be within a 24-hour period.");

            return $"{time.Seconds} {time.Minutes} {time.Hours} * * *";
        }

        /// <summary>
        /// Every week at specific times, at specific day(s)
        /// </summary>
        /// <param name="time"></param>
        /// <param name="day"></param>
        /// <param name="additionalDays"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public static string Weekly(TimeSpan time, DayOfWeek day, params DayOfWeek[] additionalDays)
        {
            if (time.TotalHours >= 24)
                throw new ArgumentOutOfRangeException(nameof(time), "Time must be within a 24-hour period.");

            DayOfWeek[] days = [ day, .. additionalDays ];

            // Map DayOfWeek (0=Sunday, 6=Saturday) to cron format (0=Sunday, 6=Saturday)
            var dayValues = days
                .Distinct()
                .Select(d => ((int)d).ToString())
                .OrderBy(s => s);

            var daysField = string.Join(",", dayValues);

            return $"{time.Seconds} {time.Minutes} {time.Hours} * * {daysField}";
        }
    }

}
