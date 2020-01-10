// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.ContainerManagement
{
    public class LinuxFunctionExecutionActivityPublisher : IHostedService, IDisposable, ILinuxFunctionExecutionActivityPublisher
    {
        private const int FlushIntervalMs = 5 * 1000; // 5 seconds

        private readonly IMeshInitServiceClient _meshInitServiceClient;
        private readonly IEnvironment _environment;
        private readonly ILogger<LinuxFunctionExecutionActivityPublisher> _logger;
        private readonly int _flushIntervalMs;
        private readonly ConcurrentQueue<ContainerFunctionExecutionActivity> _queue;
        private readonly IScriptWebHostEnvironment _webHostEnvironment;
        private Timer _timer;
        private int _flushInProgress;

        public LinuxFunctionExecutionActivityPublisher(IMeshInitServiceClient meshInitServiceClient,
            IScriptWebHostEnvironment webHostEnvironment, IEnvironment environment,
            ILogger<LinuxFunctionExecutionActivityPublisher> logger, int flushIntervalMs = FlushIntervalMs)
        {
            _meshInitServiceClient = meshInitServiceClient;
            _environment = environment;
            _logger = logger;
            _flushIntervalMs = flushIntervalMs;
            _timer = new Timer(OnTimer, null, Timeout.Infinite, Timeout.Infinite);
            _queue = new ConcurrentQueue<ContainerFunctionExecutionActivity>();
            _webHostEnvironment = webHostEnvironment ?? throw new ArgumentNullException(nameof(webHostEnvironment));
            _flushInProgress = 0;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            if (_environment.IsLinuxConsumption())
            {
                _logger.LogInformation($"Starting {nameof(LinuxFunctionExecutionActivityPublisher)}");

                // start the timer by setting the due time
                SetTimerInterval(_flushIntervalMs);
            }

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            // stop the timer if it has been started
            _timer?.Change(Timeout.Infinite, Timeout.Infinite);

            return Task.CompletedTask;
        }

        private async void OnTimer(object state)
        {
            await FlushFunctionExecutionActivities();
            SetTimerInterval(_flushIntervalMs);
        }

        private async Task FlushFunctionExecutionActivities()
        {
            try
            {
                if (Interlocked.CompareExchange(ref _flushInProgress, 1, 0) == 0)
                {
                    try
                    {
                        var uniqueActivities = new HashSet<ContainerFunctionExecutionActivity>();

                        while (_queue.TryDequeue(out var a))
                        {
                            uniqueActivities.Add(a);
                        }

                        _logger.LogInformation($"Flushing {uniqueActivities.Count} function activities");

                        if (uniqueActivities.Any())
                        {
                            await _meshInitServiceClient.PublishContainerFunctionExecutionActivities(uniqueActivities);
                        }
                    }
                    finally
                    {
                        _flushInProgress = 0;
                    }
                }
            }
            catch (Exception exc) when (!exc.IsFatal())
            {
                _logger.LogError(exc, $"{nameof(FlushFunctionExecutionActivities)}");
            }
        }

        private void SetTimerInterval(int dueTime)
        {
            var timer = _timer;
            try
            {
                timer?.Change(dueTime, Timeout.Infinite);
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

        public void Dispose()
        {
            if (_timer != null)
            {
                _timer?.Dispose();
                _timer = null;
            }
        }

        public void PublishFunctionExecutionActivity(ContainerFunctionExecutionActivity activity)
        {
            if (!_webHostEnvironment.InStandbyMode)
            {
                _queue.Enqueue(activity);
            }
        }
    }
}
