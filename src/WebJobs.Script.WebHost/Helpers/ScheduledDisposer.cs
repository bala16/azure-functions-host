// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Helpers
{
    public class ScheduledDisposer : IDisposable
    {
        private static readonly object Lock = new object();

        private readonly ILogger<ScheduledDisposer> _logger;
        private readonly Timer _timer;
        private IDisposable _host;

        public ScheduledDisposer(ILogger<ScheduledDisposer> logger)
        {
            _logger = logger;
            _host = null;
            _timer = new Timer(OnTimer, null, Timeout.Infinite, Timeout.Infinite);
        }

        public bool TryDisposeWithDelay(IDisposable host)
        {
            if (Monitor.TryEnter(Lock))
            {
                try
                {
                    if (_host == null)
                    {
                        _logger.LogDebug($"Scheduling delayed dispose");
                        _host = host;
                        SetTimerInterval(3 * 60 * 60);
                        return true;
                    }
                    else
                    {
                        _logger.LogDebug($"Dispose in progress. Failed to schedule dispose.");
                        return false;
                    }
                }
                finally
                {
                    Monitor.Exit(Lock);
                }
            }

            _logger.LogDebug("Failed to acquire lock. Couldn't schedule delayed dispose");
            return false;
        }

        private void SetTimerInterval(int dueTimeMs)
        {
            var timer = _timer;
            try
            {
                timer?.Change(dueTimeMs, Timeout.Infinite);
            }
            catch (ObjectDisposedException)
            {
                // might race with dispose
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"{nameof(SetTimerInterval)}");
            }
        }

        private void OnTimer(object state)
        {
            _logger.LogDebug($"Start {nameof(OnTimer)}");
            try
            {
                if (Monitor.TryEnter(Lock, -1))
                {
                    try
                    {
                        _host?.Dispose();
                        _host = null;
                        // _timer.Change(Timeout.Infinite, Timeout.Infinite);
                    }
                    finally
                    {
                        Monitor.Exit(Lock);
                    }
                }
                else
                {
                    _logger.LogError($"Unable to acquire lock to dispose host.");
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to dispose host");
            }
        }

        public void Dispose()
        {
            _logger.LogDebug($"start {nameof(ScheduledDisposer)}.{nameof(Dispose)}");
            _host?.Dispose();
            _timer?.Dispose();
        }
    }
}