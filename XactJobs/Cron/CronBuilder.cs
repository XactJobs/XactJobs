namespace XactJobs.Cron
{
    public static class CronBuilder
    {
        // Every X seconds (0-59)
        public static string EverySeconds(int seconds)
        {
            if (seconds <= 0 || seconds > 59)
                throw new ArgumentOutOfRangeException(nameof(seconds), "Seconds must be between 1 and 59.");

            return $"*/{seconds} * * * * *";
        }

        // Every X minutes (0-59)
        public static string EveryMinutes(int minutes)
        {
            if (minutes <= 0 || minutes > 59)
                throw new ArgumentOutOfRangeException(nameof(minutes), "Minutes must be between 1 and 59.");

            return $"0 */{minutes} * * * *";
        }

        // Every X hours (0-23)
        public static string EveryHours(int hours)
        {
            if (hours <= 0 || hours > 23)
                throw new ArgumentOutOfRangeException(nameof(hours), "Hours must be between 1 and 23.");

            return $"0 0 */{hours} * * *";
        }

        // Every day at specific time (HH:mm:ss)
        public static string EveryDayAt(TimeSpan time)
        {
            if (time.TotalHours >= 24)
                throw new ArgumentOutOfRangeException(nameof(time), "Time must be within a 24-hour period.");

            return $"{time.Seconds} {time.Minutes} {time.Hours} * * *";
        }
    }

}
