// Copyright (c) 2025 XactJobs
// Licensed under the MIT License. See LICENSE in the repository root.

namespace XactJobs
{
    /// <summary>
    /// Makes the method a periodic job, found by <c>WithPeriodicJobsFromAssembly</c> or <c>WithPeriodicJobsFromAssemblyOf&lt;T&gt;</c>.
    /// It's the same as <c>WithPeriodicJob</c> with a call of the method: moving a job between the two keeps its definition.
    /// <para>
    /// The method is public: static, or of a concrete class with a public constructor (the worker creates it, with the constructor's
    /// parameters from the services), not generic, returning void or a Task. Its parameters can be a <see cref="CancellationToken"/>,
    /// an <see cref="XactJobContext"/>, and one string, which gets <see cref="Config"/>. A method can have several of these attributes,
    /// each with its own id, e.g. with different configs.
    /// </para>
    /// <para>
    /// Removing the attribute doesn't delete the periodic job: its runs keep running. Delete it by its id in its place
    /// (<c>JobDeletePeriodicAsync</c>).
    /// </para>
    /// </summary>
    /// <param name="id">The periodic job's id: the same on every node, and unique among the attributes and <c>WithPeriodicJob</c>.</param>
    /// <param name="cronExpression">The schedule, in UTC, with seconds, e.g. one of <see cref="CronExpressions"/>.</param>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
    public sealed class XactPeriodicJobAttribute(string id, string cronExpression) : Attribute
    {
        public string Id { get; } = id;

        public string CronExpression { get; } = cronExpression;

        /// <summary>
        /// The queue: the default queue if it's not set, or an isolated queue, which must be configured.
        /// </summary>
        public string? Queue { get; set; }

        /// <summary>
        /// Passed to the method's string parameter, which it needs then. Stored with the periodic job's definition: changing it
        /// changes the definition, like an argument of <c>WithPeriodicJob</c>.
        /// </summary>
        public string? Config { get; set; }
    }
}
