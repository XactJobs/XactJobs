// Copyright (c) 2025 XactJobs
// Licensed under the MIT License. See LICENSE in the repository root.

namespace XactJobs.CronUtil
{
    internal static class CronTimeZones
    {
        /// <summary>
        /// The longest time zone id stored (the IANA ids have at most about 30 characters).
        /// </summary>
        public const int MaxIdLength = 100;

        /// <summary>
        /// The time zone of a stored id: UTC for null.
        /// </summary>
        /// <exception cref="TimeZoneNotFoundException">The system doesn't know it.</exception>
        public static TimeZoneInfo Find(string? id)
        {
            return id == null ? TimeZoneInfo.Utc : TimeZoneInfo.FindSystemTimeZoneById(id);
        }

        /// <summary>
        /// The id to store: null for UTC, or a zone with the same rules (so that it's the same definition as no time zone), an IANA id
        /// for a Windows one when the system can convert it (so that nodes on Windows and Linux store the same), otherwise the id given.
        /// </summary>
        /// <exception cref="TimeZoneNotFoundException">The system doesn't know it.</exception>
        public static string? Normalize(string? id)
        {
            if (id == null) return null;

            ArgumentException.ThrowIfNullOrWhiteSpace(id);

            var zone = TimeZoneInfo.FindSystemTimeZoneById(id);

            if (zone.HasSameRules(TimeZoneInfo.Utc)) return null;

            var normalized = TimeZoneInfo.TryConvertWindowsIdToIanaId(zone.Id, out var ianaId) ? ianaId : zone.Id;

            if (normalized.Length > MaxIdLength)
            {
                throw new ArgumentException($"The time zone id '{normalized}' is longer than {MaxIdLength} characters.", nameof(id));
            }

            // the id stored must be found again, e.g. not the "Local" of a system without a named local time zone
            TimeZoneInfo.FindSystemTimeZoneById(normalized);

            return normalized;
        }
    }
}
