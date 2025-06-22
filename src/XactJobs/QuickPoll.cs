using Microsoft.EntityFrameworkCore;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using XactJobs.Annotations;

namespace XactJobs
{
    public class QuickPollChannels
    {
        private readonly Dictionary<string, QuickPollChannel> _channels;

        public QuickPollChannels(Dictionary<string, QuickPollChannel> channels)
        {
            _channels = channels;
        }

        public bool TryGetChannel(string queue, [NotNullWhen(true)] out QuickPollChannel? channel)
        {
            return _channels.TryGetValue(queue, out channel);
        }

        public bool TryNotify(string queue)
        {
            if (!_channels.TryGetValue(queue, out var additionalChannel)) return false;

            additionalChannel.Notify();
            return true;
        }
    }

    public class QuickPoll<TDbContext> where TDbContext : DbContext
    {
        private readonly Dictionary<string, bool> _affectedQueues = [];
        private readonly QuickPollChannels _quickPollChannels;

        public TDbContext DbContext { get; }

        public QuickPoll(TDbContext dbContext, QuickPollChannels quickPollChannels)
        {
            DbContext = dbContext;
            _quickPollChannels = quickPollChannels;
        }

        /// <summary>
        /// Enqueues a background job and registers it for QuickPoll notification on SaveChanges or Commit
        /// </summary>
        /// <param name="jobExpression"></param>
        /// <param name="queue"></param>
        /// <returns></returns>
        public XactJob JobEnqueue([InstantHandle] Expression<Action> jobExpression, string? queue = null)
        {
            return JobAdd(jobExpression, queue);
        }

        /// <summary>
        /// Enqueues a background job and registers it for QuickPoll notification on SaveChanges or Commit
        /// </summary>
        /// <param name="jobExpression"></param>
        /// <param name="queue"></param>
        /// <returns></returns>
        public XactJob JobEnqueue<T>([InstantHandle] Expression<Action<T>> jobExpression, string? queue = null)
        {
            return JobAdd(jobExpression, queue);
        }

        /// <summary>
        /// Enqueues a background job and registers it for QuickPoll notification on SaveChanges or Commit
        /// </summary>
        /// <param name="jobExpression"></param>
        /// <param name="queue"></param>
        /// <returns></returns>
        public XactJob JobEnqueue( [InstantHandle] Expression<Func<Task>> jobExpression, string? queue = null)
        {
            return JobAdd(jobExpression, queue);
        }

        /// <summary>
        /// Enqueues a background job and registers it for QuickPoll notification on SaveChanges or Commit
        /// </summary>
        /// <param name="jobExpression"></param>
        /// <param name="queue"></param>
        /// <returns></returns>
        public XactJob JobEnqueue<T>([InstantHandle] Expression<Func<T, Task>> jobExpression, string? queue = null)
        {
            return JobAdd(jobExpression, queue);
        }

        /// <summary>
        /// Calls DbContext.SaveChangesAsync and notifies job workers to perform a quick poll for the affected queues.
        /// This will work only if jobs were added with <see cref="QuickPoll.JobEnqueue"/>.
        /// If there is a current transaction, the notification will not be performed until <see cref="QuickPoll.CommitAndNotifyAsync"/> is called.
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task SaveChangesAndNotifyAsync(CancellationToken cancellationToken)
        {
            await DbContext.SaveChangesAsync(cancellationToken)
                .ConfigureAwait(false);

            // only notify if not in transaction (CommitAndNotifyAsync should be used to notify when in transaction)
            if (DbContext.Database.CurrentTransaction == null)
            {
                Notify();
            }
        }

        /// <summary>
        /// Calls DbContext.SaveChanges and notifies job workers to perform a quick poll for the affected queues.
        /// This will work only if jobs were added with <see cref="QuickPoll.JobEnqueue"/>.
        /// If there is a current transaction, the notification will not be performed until <see cref="QuickPoll.CommitAndNotifyAsync"/> is called.
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public void SaveChangesAndNotify()
        {
            DbContext.SaveChanges();

            // only notify if not in transaction (CommitAndNotifyAsync should be used to notify when in transaction)
            if (DbContext.Database.CurrentTransaction == null)
            {
                Notify();
            }
        }

        /// <summary>
        /// Calls DbContext.Database.CurrentTransaction.CommitAsync and notifies job workers to perform a quick poll for the affected queues.
        /// This will work only if jobs were added with <see cref="QuickPoll.JobEnqueue"/>.
        /// If there is no current transaction, then only notification is performed.
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task CommitAndNotifyAsync(CancellationToken cancellationToken)
        {
            if (DbContext.Database.CurrentTransaction != null)
            {
                await DbContext.Database.CurrentTransaction.CommitAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            Notify();
        }

        /// <summary>
        /// Calls DbContext.Database.CurrentTransaction.Commit and notifies job workers to perform a quick poll for the affected queues.
        /// This will work only if jobs were added with <see cref="QuickPoll.JobEnqueue"/>.
        /// If there is no current transaction, then only notification is performed.
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public void CommitAndNotify()
        {
            DbContext.Database.CurrentTransaction?.Commit();
            Notify();
        }

        /// <summary>
        /// This is called automatically by <see cref="QuickPoll.SaveChangesAndNotifyAsync" /> and <see cref="QuickPoll.CommitAndNotifyAsync"/>.
        /// No need to call it explicitely, unless saving and committing is done directly through the DbContext.
        /// </summary>
        /// <param name="queueNames"></param>
        public void Notify(params string[] queueNames)
        {
            foreach (var queue in _affectedQueues.Keys.Union(queueNames))
            {
                _quickPollChannels.TryNotify(queue);
            }

            _affectedQueues.Clear();
        }

        private XactJob JobAdd(LambdaExpression lambdaExp, string? queue)
        {
            queue ??= QueueNames.Default;

            _affectedQueues[queue] = true;

            return DbContext.JobAdd(lambdaExp, null, queue);
        }
    }
}
