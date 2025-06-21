using Microsoft.EntityFrameworkCore;
using System.Threading.Channels;

namespace XactJobs
{
    public class QuickPollChannel
    {
        private readonly SemaphoreSlim _batchLock = new(1, 1);
        private readonly Channel<bool> _notificationChannel;

        public QuickPollChannel(int capacity)
        {
            _notificationChannel = Channel.CreateBounded<bool>(new BoundedChannelOptions(capacity) 
            { 
                FullMode = BoundedChannelFullMode.DropOldest 
            });
        }

        internal void Notify()
        {
            _notificationChannel.Writer.TryWrite(true);
        }

        internal ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken)
        {
            return _notificationChannel.Reader.WaitToReadAsync(cancellationToken);
        }

        internal async Task<int> ConsumeBatchAsync(int batchSize, CancellationToken cancellationToken)
        {
            int consumedCount = 0;

            try
            {
                await _batchLock.WaitAsync(cancellationToken);

                while (consumedCount < batchSize && _notificationChannel.Reader.TryRead(out _))
                {
                    consumedCount++;
                }
            }
            finally
            {
                _batchLock.Release();
            }

            return consumedCount;
        }
    }

    public class QuickPollChannels
    {
        public Dictionary<string, QuickPollChannel> Channels = [];
    }

    public class QuickPoll<TDbContext> where TDbContext : DbContext
    {
        private readonly IReadOnlyDictionary<string, QuickPollChannel> _channels;

        public TDbContext DbContext { get; }

        public QuickPoll(TDbContext dbContext, QuickPollChannels quickPollChannels)
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
                channel.Notify();
            }

            foreach (var additionalQueue in additionalQueueNames)
            {
                if (_channels.TryGetValue(additionalQueue, out var additionalChannel))
                {
                    additionalChannel.Notify();
                }
            }
        }
    }
}
