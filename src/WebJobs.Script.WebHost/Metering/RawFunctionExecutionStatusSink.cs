// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Metering
{
    public class RawFunctionExecutionStatusSink : IRawFunctionExecutionStatusSink
    {
        private readonly ILogger<RawFunctionExecutionStatusSink> _logger;
        // keep last 2 minutes worth of data. so no need to have blocking collection

        // this has no bound. but is synchronized.
        // might be possible to add bound using
        // https://docs.microsoft.com/en-us/dotnet/standard/collections/thread-safe/how-to-add-bounding-and-blocking
        private readonly ConcurrentQueue<TrackedFunctionExecutionActivity> _activities;
        // queue is better here. since the only operation we will do it dequeue which doesnt need to shift elements around unlike a list

        public RawFunctionExecutionStatusSink(ILogger<RawFunctionExecutionStatusSink> logger)
        {
            _logger = logger;
            _activities = new ConcurrentQueue<TrackedFunctionExecutionActivity>();
        }

        public void TryAddFunctionActivityWithTime(TrackedFunctionExecutionActivity activity)
        {
            // handle the case where reader has crashed and is not removing anything.
            // may be check if the count > some large number clear everything past last 2 minutes.
            _activities.Enqueue(activity);
        }

        public IReadOnlyCollection<TrackedFunctionExecutionActivity> GetActivities()
        {
            var activities = new List<TrackedFunctionExecutionActivity>();

            var twoMinutesBack = DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(2));

            int staleCount = 0;

            while (_activities.TryDequeue(out var activity))
            {
                if (activity.TimeStamp < twoMinutesBack)
                {
                    staleCount++;
                }
                else
                {
                    activities.Add(activity);
                }
            }

            if (staleCount > 0)
            {
                _logger.LogInformation("Stale counts = " + staleCount);
            }

            return activities;
        }
    }
}