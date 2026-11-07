// Copyright (c) 2025 XactJobs
// Licensed under the MIT License. See LICENSE in the repository root.

namespace XactJobs
{
    /// <summary>
    /// Thrown by a job that can never succeed, to stop it without retries.
    /// <para>
    /// The job moves to the history as cancelled right away, with this exception's message as the error,
    /// instead of being rescheduled by the retry strategy. Like with any failure, the job's database writes are rolled back.
    /// A run of a periodic job is cancelled alone: the next run is scheduled as usual.
    /// </para>
    /// It's also found when it's wrapped in other exceptions (inner exceptions, or an <see cref="AggregateException"/>).
    /// </summary>
    public class XactJobCancelException : Exception
    {
        public XactJobCancelException(string message)
            : base(message)
        {
        }

        public XactJobCancelException(string message, Exception? innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        /// Finds the exception in the given one or in the exceptions it wraps.
        /// </summary>
        internal static XactJobCancelException? Find(Exception? ex)
        {
            // depth-first, with a guard against cycles and very deep chains
            var pending = new Stack<Exception>();
            var visited = 0;

            if (ex != null) pending.Push(ex);

            while (pending.Count > 0 && visited++ < 100)
            {
                var current = pending.Pop();

                if (current is XactJobCancelException cancel) return cancel;

                if (current is AggregateException aggregate)
                {
                    foreach (var inner in aggregate.InnerExceptions.Reverse())
                    {
                        pending.Push(inner);
                    }
                }
                else if (current.InnerException != null)
                {
                    pending.Push(current.InnerException);
                }
            }

            return null;
        }
    }
}
