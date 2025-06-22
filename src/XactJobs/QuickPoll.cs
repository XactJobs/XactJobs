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

        public XactJob JobEnqueue([InstantHandle] Expression<Action> jobExpression, string? queue = null)
        {
            return JobAdd(jobExpression, queue);
        }

        public XactJob JobEnqueue<T>([InstantHandle] Expression<Action<T>> jobExpression, string? queue = null)
        {
            return JobAdd(jobExpression, queue);
        }

        public XactJob JobEnqueue( [InstantHandle] Expression<Func<Task>> jobExpression, string? queue = null)
        {
            return JobAdd(jobExpression, queue);
        }

        public XactJob JobEnqueue<T>([InstantHandle] Expression<Func<T, Task>> jobExpression, string? queue = null)
        {
            return JobAdd(jobExpression, queue);
        }

        public async Task SaveChangesAndNotifyAsync(CancellationToken cancellationToken)
        {
            await DbContext.SaveChangesAsync(cancellationToken)
                .ConfigureAwait(false);

            Notify();
        }

        public async Task CommitAndNotifyAsync(CancellationToken cancellationToken)
        {
            if (DbContext.Database.CurrentTransaction == null) throw new Exception($"Cannot commit - current transaction in DbContext is null");

            await DbContext.Database.CurrentTransaction.CommitAsync(cancellationToken)
                .ConfigureAwait(false);

            Notify();
        }

        public void Notify(params string[] queueNames)
        {
            foreach (var queue in _affectedQueues.Keys.Union(queueNames))
            {
                _quickPollChannels.TryNotify(queue);
            }
        }

        private XactJob JobAdd(LambdaExpression lambdaExp, string? queue)
        {
            queue ??= QueueNames.Default;

            _affectedQueues[queue] = true;

            return DbContext.JobAdd(lambdaExp, null, queue);
        }
    }
}
