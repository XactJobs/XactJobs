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

namespace XactJobs
{
    public interface IRetryStrategy
    {
        DateTime? GetRetryTimeUtc(XactJob job, int newErrorCount);
    }

    public class DefaultRetryStrategy : IRetryStrategy
    {
        public int MaxAttempts { get; }
        public IReadOnlyList<int> RetrySeconds { get; }

        public DefaultRetryStrategy(int maxAttempts = 10, int[]? retrySeconds = null)
        {
            MaxAttempts = maxAttempts;
            RetrySeconds = retrySeconds ?? [2, 2, 5, 10, 30, 60, 5 * 60, 15 * 60, 30 * 60, 60 * 60]; 
        }

        public DateTime? GetRetryTimeUtc(XactJob job, int newErrorCount)
        {
            if (newErrorCount >= MaxAttempts)
            {
                return null;
            }

            var seconds = newErrorCount <= RetrySeconds.Count
                ? RetrySeconds[newErrorCount - 1]
                : RetrySeconds[^1];

            return DateTime.UtcNow.AddSeconds(seconds);
        }
    }
}
