// Copyright (c) 2025 XactJobs
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Collections;

namespace XactJobs.Internal
{
    /// <summary>
    /// The log scope of a job's attempt: its properties are on every entry logged while it runs, the job's own and EF's included,
    /// with a logging provider that records scopes (e.g. the console's IncludeScopes, Serilog, OpenTelemetry's IncludeScopes).
    /// </summary>
    internal sealed class XactJobLogScope(XactJob job, string queue) : IReadOnlyList<KeyValuePair<string, object?>>
    {
        public int Count => 5;

        public KeyValuePair<string, object?> this[int index] => index switch
        {
            0 => new("JobId", job.Id),
            1 => new("JobType", job.TypeName),
            2 => new("JobMethod", job.MethodName),
            3 => new("JobQueue", queue),
            4 => new("JobAttempt", job.ErrorCount + 1),
            _ => throw new ArgumentOutOfRangeException(nameof(index)),
        };

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
        {
            for (var i = 0; i < Count; i++)
            {
                yield return this[i];
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public override string ToString() => $"Job {job.Id} {job.TypeName}.{job.MethodName}, queue {queue}, attempt {job.ErrorCount + 1}";
    }
}
