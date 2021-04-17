// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Helpers
{
    public class ScheduledDisposer : IScheduledDisposer, IDisposable
    {
        private const int DelayTimeMs = 5 * 1000;

        private readonly ILogger<ScheduledDisposer> _logger;
        private readonly SemaphoreHelper _semaphoreHelper;
        private readonly int _delayTimeMs;
        private SemaphoreSlim _semaphore;
        private Timer _timer;
        private IDisposable _disposable;

        public ScheduledDisposer(ILogger<ScheduledDisposer> logger, SemaphoreHelper semaphoreHelper, int delayTimeMs = DelayTimeMs)
        {
            _logger = logger;
            _semaphoreHelper = semaphoreHelper;
            _delayTimeMs = delayTimeMs;
            _disposable = null;
            _timer = new Timer(OnDisposeTimer, null, Timeout.Infinite, Timeout.Infinite);
            _semaphore = new SemaphoreSlim(1, 1);
        }

        private bool TryScheduleDispose(IDisposable disposable)
        {
            if (_semaphoreHelper.Wait(_semaphore, 0))
            {
                try
                {
                    if (_disposable == null)
                    {
                        _logger.LogDebug("Scheduling delayed dispose.");
                        _disposable = disposable;
                        ScheduleTimer();
                        return true;
                    }
                    else
                    {
                        _logger.LogDebug("Dispose in progress.");
                        return false;
                    }
                }
                finally
                {
                    _semaphoreHelper.Release(_semaphore);
                }
            }

            _logger.LogDebug("Failed to acquire lock.");
            return false;
        }

        public bool ScheduleDispose(IDisposable disposable)
        {
            var disposeScheduled = false;

            try
            {
                disposeScheduled = TryScheduleDispose(disposable);
            }
            catch (Exception e)
            {
                // fallback to disposing on current thread
                _logger.LogError(e, $"Failed to {nameof(TryScheduleDispose)}");
            }

            if (!disposeScheduled)
            {
                _logger.LogDebug($"Disposing {nameof(disposable)} on current thread.");
                disposable.Dispose();
            }

            return disposeScheduled;
        }

        private void ScheduleTimer()
        {
            var timer = _timer;
            try
            {
                timer?.Change(_delayTimeMs, Timeout.Infinite);
            }
            catch (ObjectDisposedException)
            {
                // might race with dispose
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"{nameof(ScheduleTimer)}");
            }
        }

        private void OnDisposeTimer(object state)
        {
            try
            {
                _logger.LogDebug($"Triggering {nameof(OnDisposeTimer)}");
                if (_semaphoreHelper.Wait(_semaphore, -1))
                {
                    try
                    {
                        _disposable?.Dispose();
                        _disposable = null;
                    }
                    finally
                    {
                        _semaphoreHelper.Release(_semaphore);
                    }
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, nameof(OnDisposeTimer));
            }
        }

        public void Dispose()
        {
            if (_timer != null)
            {
                _timer.Dispose();
                _timer = null;
            }

            if (_semaphore != null)
            {
                _semaphore.Dispose();
                _semaphore = null;
            }
        }
    }
}