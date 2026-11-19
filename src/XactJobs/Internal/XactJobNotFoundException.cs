// Copyright (c) 2025 XactJobs
// Licensed under the MIT License. See LICENSE in the repository root.

namespace XactJobs.Internal
{
    /// <summary>
    /// This node doesn't have the job's type, or a public method of it with the job's signature: during a rolling deployment, a job
    /// enqueued by a node with a newer version. The runner puts the job back for another node, for a while
    /// (<see cref="XactJobsOptions{TDbContext}.UnresolvedJobGracePeriodInHours"/>), and then it fails as any other attempt. Its own
    /// type, so that the job's own exceptions (a <see cref="FileNotFoundException"/>, a <see cref="MissingMethodException"/>) are never
    /// taken for it.
    /// </summary>
    internal sealed class XactJobNotFoundException(string message, Exception? innerException = null) : Exception(message, innerException)
    {
    }
}
