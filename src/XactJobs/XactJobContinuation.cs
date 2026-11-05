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
    /// When a continuation runs, once all the jobs of the batch it waits for are finished.
    /// A job of the batch failed if it ran out of retries, threw <see cref="XactJobCancelException"/>, was cancelled from the dashboard,
    /// or was itself a continuation that was skipped. Otherwise it succeeded.
    /// A continuation that doesn't run is moved to the history as <see cref="XactJobStatus.Skipped"/>.
    /// </summary>
    public enum XactJobContinuation
    {
        /// <summary>
        /// Runs if all the jobs of the batch succeeded.
        /// </summary>
        OnSuccess = 0,

        /// <summary>
        /// Runs if at least one job of the batch failed.
        /// </summary>
        OnFailure = 1,

        /// <summary>
        /// Runs when all the jobs of the batch are finished, whether they succeeded or not.
        /// </summary>
        OnFinish = 2,
    }
}
