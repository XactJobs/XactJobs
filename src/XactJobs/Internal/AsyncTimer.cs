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

using Microsoft.Extensions.Logging;

namespace XactJobs.Internal
{
    internal class AsyncTimer : IDisposable
    {
        private readonly ILogger _logger;

        private readonly TimeSpan _interval;
        private readonly Func<CancellationToken, Task> _callback;

        private CancellationTokenSource? _cts;
        private Task? _runningTask;

        public AsyncTimer(ILogger logger, TimeSpan interval, Func<CancellationToken, Task> callback)
        {
            _logger = logger;
            _interval = interval;
            _callback = callback ?? throw new ArgumentNullException(nameof(callback));
        }

        public void Start()
        {
            if (_runningTask != null && !_runningTask.IsCompleted)
                throw new InvalidOperationException("AsyncTimer is already running.");

            _cts = new CancellationTokenSource();
            _runningTask = RunAsync(_cts.Token);
        }

        private async Task RunAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_interval, token).ConfigureAwait(false);

                    await _callback(token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // expected on cancellation
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed executing AsyncTimer callback");
                }
            }
        }

        public async Task StopAsync()
        {
            if (_cts == null)
                return;

            _cts.Cancel();

            try
            {
                if (_runningTask != null)
                    await _runningTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // expected on cancellation
            }
            finally
            {
                _cts.Dispose();
                _cts = null;
                _runningTask = null;
            }
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _cts?.Dispose();
        }
    }
}
