// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Hosting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Metering
{
    public class FunctionExecutionMeteringOptions : IOptionsFormatter
    {
        private TimeSpan _functionExecutionMetricsSamplingInterval;

        public FunctionExecutionMeteringOptions()
        {
            FunctionExecutionMetricsSamplingInterval = TimeSpan.FromSeconds(10);
        }

        /// <summary>
        /// Gets or sets the sampling interval for function execution metrics.
        /// </summary>
        public TimeSpan FunctionExecutionMetricsSamplingInterval
        {
            get
            {
                return _functionExecutionMetricsSamplingInterval;
            }

            set
            {
                if (value < TimeSpan.FromSeconds(1) || value > TimeSpan.FromSeconds(30))
                {
                    throw new ArgumentOutOfRangeException(nameof(FunctionExecutionMetricsSamplingInterval));
                }
                _functionExecutionMetricsSamplingInterval = value;
            }
        }

        public string Format()
        {
            var options = new JObject
            {
                { nameof(FunctionExecutionMetricsSamplingInterval), FunctionExecutionMetricsSamplingInterval }
            };

            return options.ToString(Formatting.Indented);
        }
    }
}