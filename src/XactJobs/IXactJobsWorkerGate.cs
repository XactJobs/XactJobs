// Copyright (c) 2025 XactJobs
// Licensed under the MIT License. See LICENSE in the repository root.

namespace XactJobs
{
    /// <summary>
    /// Decides at run time whether this node's workers may claim jobs, registered with <c>WithWorkerGate&lt;T&gt;()</c>: e.g. so that a
    /// node marked inactive in the app's database doesn't take jobs from the others.
    /// <para>
    /// Asked by every worker of every queue before each claim, also when a QuickPoll notification or a finished batch wakes it. False
    /// claims nothing, and the worker asks again at its next poll. Only claiming stops: the jobs the worker already holds or runs finish,
    /// and their leases are extended as usual. An exception counts as false and is logged. It's called often (once per poll and worker),
    /// so keep it cheap, e.g. cache a value read from the database for a few seconds.
    /// </para>
    /// </summary>
    public interface IXactJobsWorkerGate
    {
        /// <param name="queueName">The worker's queue: <see cref="QueueNames.Default"/> or an isolated queue's name.</param>
        /// <param name="cancellationToken">Cancelled when the app stops.</param>
        ValueTask<bool> CanClaimAsync(string queueName, CancellationToken cancellationToken);
    }
}
