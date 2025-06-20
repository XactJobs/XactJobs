using Microsoft.Extensions.Logging;

namespace XactJobs
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
