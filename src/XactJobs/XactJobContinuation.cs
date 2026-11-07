// Copyright (c) 2025 XactJobs
// Licensed under the MIT License. See LICENSE in the repository root.

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
