// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Metering
{
    public class FunctionExecutionMeteringService : IHostedService, IDisposable
    {
        private readonly IEnvironment _environment;
        private readonly IPrimaryHostStateProvider _primaryHostStateProvider;
        private readonly IRawFunctionExecutionStatusSink _functionExecutionStatusSink;
        private readonly FunctionExecutionMeteringOptions _functionExecutionMeteringOptions;
        private readonly ILogger<FunctionExecutionMeteringService> _logger;

        private readonly TimeSpan _interval;
        private readonly Timer _timer;
        private bool _disposed;

        public FunctionExecutionMeteringService(IEnvironment environment, IPrimaryHostStateProvider primaryHostStateProvider,
            IOptions<FunctionExecutionMeteringOptions> functionExecutionMeteringOptions,
            IRawFunctionExecutionStatusSink functionExecutionStatusSink,
            ILogger<FunctionExecutionMeteringService> logger)
        {
            _environment = environment;
            _primaryHostStateProvider = primaryHostStateProvider;
            _functionExecutionStatusSink = functionExecutionStatusSink;
            _functionExecutionMeteringOptions = functionExecutionMeteringOptions.Value;
            _logger = logger;

            _interval = _functionExecutionMeteringOptions.FunctionExecutionMetricsSamplingInterval;
            _timer = new Timer(OnTimer, null, Timeout.Infinite, Timeout.Infinite);
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            if (_environment.IsLinuxConsumption())
            {
                _logger.LogInformation("Function execution metering is enabled.");

                // start the timer by setting the due time
                SetTimerInterval((int)_interval.TotalMilliseconds);
            }

            return Task.CompletedTask;
        }

        private void SetTimerInterval(int dueTime)
        {
            if (!_disposed)
            {
                var timer = _timer;
                if (timer != null)
                {
                    try
                    {
                        _timer.Change(dueTime, Timeout.Infinite);
                    }
                    catch (ObjectDisposedException)
                    {
                        // might race with dispose
                    }
                }
            }
        }

        private async void OnTimer(object state)
        {
            if (_primaryHostStateProvider.IsPrimary)
            {
                await CollectFunctionExecutionMetrics();
            }

            SetTimerInterval((int)_interval.TotalMilliseconds);
        }

        private async Task CollectFunctionExecutionMetrics()
        {
            try
            {
                var activities = _functionExecutionStatusSink.GetActivities();

                _logger.LogInformation("Activities count " + activities.Count);

                if (activities.Any())
                {

                }

            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to " + nameof(CollectFunctionExecutionMetrics));
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            // stop the timer if it has been started
            _timer.Change(Timeout.Infinite, Timeout.Infinite);

            return Task.CompletedTask;
        }

        private void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _timer.Dispose();
                }

                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
    }
}
