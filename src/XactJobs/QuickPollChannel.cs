// This file is part of XactJobs.
//
// XactJobs is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as published
// by the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// XactJobs is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU Lesser General Public License for more details.
//
// You should have received a copy of the GNU Lesser General Public License
// along with this program. If not, see <https://www.gnu.org/licenses/>.

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
