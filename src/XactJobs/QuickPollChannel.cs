// Copyright (c) 2025 XactJobs
// Licensed under the MIT License. See LICENSE in the repository root.

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

        // set when a batch finished, so the next poll claims the continuations, without waiting for its regular check
        private int _continuationsReady;

        internal void Notify()
        {
            _notificationChannel.Writer.TryWrite(true);
        }

        internal void NotifyContinuationsReady()
        {
            Volatile.Write(ref _continuationsReady, 1);

            Notify();
        }

        internal bool TakeContinuationsReady()
        {
            return Interlocked.Exchange(ref _continuationsReady, 0) == 1;
        }

        internal ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken)
        {
            return _notificationChannel.Reader.WaitToReadAsync(cancellationToken);
        }

        internal async Task<int> ConsumeBatchAsync(int batchSize, CancellationToken cancellationToken)
        {
            int consumedCount = 0;

            await _batchLock.WaitAsync(cancellationToken)
                .ConfigureAwait(false);

            try
            {
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
