// Copyright (c) 2025 XactJobs
// Licensed under the MIT License. See LICENSE in the repository root.

namespace XactJobs
{
    /// <summary>
    /// Common schedules as constants, for <see cref="XactPeriodicJobAttribute"/>, whose arguments must be constants. In UTC, with seconds.
    /// <see cref="Cron"/> builds other ones.
    /// </summary>
    public static class CronExpressions
    {
        public const string EverySecond = "* * * * * *";

        public const string EveryMinute = "0 * * * * *";

        public const string Hourly = "0 0 * * * *";

        /// <summary>At midnight.</summary>
        public const string Daily = "0 0 0 * * *";

        /// <summary>On Sundays at midnight.</summary>
        public const string Weekly = "0 0 0 * * 0";

        /// <summary>On the first day of the month at midnight.</summary>
        public const string Monthly = "0 0 0 1 * *";
    }
}
