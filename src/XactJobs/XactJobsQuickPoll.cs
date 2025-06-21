using Microsoft.EntityFrameworkCore;
using System.Threading.Channels;

namespace XactJobs
{
    public class XactJobsQuickPollChannels
    {
        public Dictionary<string, Channel<bool>> Channels = [];
    }

    public class XactJobsQuickPoll<TDbContext> where TDbContext : DbContext
    {
        private readonly IReadOnlyDictionary<string, Channel<bool>> _channels;

        public TDbContext DbContext { get; }

        public XactJobsQuickPoll(TDbContext dbContext, XactJobsQuickPollChannels quickPollChannels)
        {
            DbContext = dbContext;
            _channels = quickPollChannels.Channels;
        }

        public async Task SaveChangesAndNotifyAsync(CancellationToken cancellationToken, string? queueName = null, params string[] additionalQueueNames)
        {
            await DbContext.SaveChangesAsync(cancellationToken)
                .ConfigureAwait(false);

            NotifyJobsAdded(queueName, additionalQueueNames);
        }

        public async Task CommitAndNotifyAsync(CancellationToken cancellationToken, string? queueName = null, params string[] additionalQueueNames)
        {
            if (DbContext.Database.CurrentTransaction == null) throw new Exception($"Cannot commit - current transaction in DbContext is null");

            await DbContext.Database.CurrentTransaction.CommitAsync(cancellationToken)
                .ConfigureAwait(false);

            NotifyJobsAdded(queueName, additionalQueueNames);
        }

        public void NotifyJobsAdded(string? queueName = null, params string[] additionalQueueNames)
        {
            if (_channels.TryGetValue(queueName ?? QueueNames.Default, out var channel))
            {
                channel.Writer.TryWrite(true);
            }

            foreach (var additionalQueue in additionalQueueNames)
            {
                if (_channels.TryGetValue(additionalQueue, out var additionalChannel))
                {
                    additionalChannel.Writer.TryWrite(true);
                }
            }
        }
    }
}
