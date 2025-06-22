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
}
