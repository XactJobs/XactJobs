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
    /// <summary>
    /// What the worker does when a job calls <c>SaveChanges</c> on its scoped <c>TDbContext</c> outside a transaction.
    /// Those changes are committed right away, so if the job runs again, e.g. after its lease expired, they happen again.
    /// </summary>
    public enum XactJobSaveCheck
    {
        /// <summary>
        /// Logs a warning, once per job method per worker host. The default.
        /// </summary>
        Warn = 0,

        /// <summary>
        /// Throws an <see cref="InvalidOperationException"/> from <c>SaveChanges</c>, so nothing is saved, and the job fails and is retried.
        /// </summary>
        Throw = 1,

        /// <summary>
        /// Doesn't check.
        /// </summary>
        Ignore = 2,
    }
}
