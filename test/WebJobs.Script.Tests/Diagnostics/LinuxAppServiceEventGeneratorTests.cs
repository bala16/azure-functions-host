// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Diagnostics
{
    public class LinuxAppServiceEventGeneratorTests
    {
        private readonly LinuxAppServiceEventGenerator _generator;
        private readonly Dictionary<string, List<string>> _events;

        public LinuxAppServiceEventGeneratorTests()
        {
            _events = new Dictionary<string, List<string>>
            {
                [LinuxEventGenerator.FunctionsLogsFileName] = new List<string>(),
                [LinuxEventGenerator.FunctionsMetricsFileName] = new List<string>(),
                [LinuxEventGenerator.FunctionsDetailsFileName] = new List<string>()
            };

            _generator = new LinuxAppServiceEventGenerator((fileName, logEntry) =>
            {
                _events[fileName].Add(logEntry);
            });
        }

        [Theory]
        [MemberData(nameof(LinuxEventGeneratorTestData.GetLogEvents), MemberType = typeof(LinuxEventGeneratorTestData))]
        public void ParseLogEvents(LogLevel level, string subscriptionId, string appName, string functionName, string eventName, string source, string details, string summary, string exceptionType, string exceptionMessage, string functionInvocationId, string hostInstanceId, string activityId)
        {
            _generator.LogFunctionTraceEvent(level, subscriptionId, appName, functionName, eventName, source, details, summary, exceptionType, exceptionMessage, functionInvocationId, hostInstanceId, activityId);

            var evt = _events[LinuxEventGenerator.FunctionsLogsFileName].Single();

            var regex = new Regex(LinuxAppServiceEventGenerator.TraceEventRegex);
            var match = regex.Match(evt);

            Assert.True(match.Success);
            Assert.Equal(16, match.Groups.Count);

            DateTime dt;
            var groupMatches = match.Groups.Select(p => p.Value).Skip(1).ToArray();
            Assert.Collection(groupMatches,
                p => Assert.Equal((int)LinuxEventGenerator.ToEventLevel(level), int.Parse(p)),
                p => Assert.Equal(subscriptionId, p),
                p => Assert.Equal(appName, p),
                p => Assert.Equal(functionName, p),
                p => Assert.Equal(eventName, p),
                p => Assert.Equal(source, p),
                p => Assert.Equal(details, p),
                p => Assert.Equal(summary, p),
                p => Assert.Equal(ScriptHost.Version, p),
                p => Assert.True(DateTime.TryParse(p, out dt)),
                p => Assert.Equal(exceptionType, p),
                p => Assert.Equal(exceptionMessage, p),
                p => Assert.Equal(functionInvocationId, p),
                p => Assert.Equal(hostInstanceId, p),
                p => Assert.Equal(activityId, p));
        }

        [Theory]
        [MemberData(nameof(LinuxEventGeneratorTestData.GetMetricEvents), MemberType = typeof(LinuxEventGeneratorTestData))]
        public void ParseMetricEvents(string subscriptionId, string appName, string functionName, string eventName, long average, long minimum, long maximum, long count)
        {
            _generator.LogFunctionMetricEvent(subscriptionId, appName, functionName, eventName, average, minimum, maximum, count, DateTime.Now);

            string evt = _events[LinuxEventGenerator.FunctionsMetricsFileName].Single();

            Regex regex = new Regex(LinuxAppServiceEventGenerator.MetricEventRegex);
            var match = regex.Match(evt);

            Assert.True(match.Success);
            Assert.Equal(11, match.Groups.Count);

            DateTime dt;
            var groupMatches = match.Groups.Select(p => p.Value).Skip(1).ToArray();
            Assert.Collection(groupMatches,
                p => Assert.Equal(subscriptionId, p),
                p => Assert.Equal(appName, p),
                p => Assert.Equal(functionName, p),
                p => Assert.Equal(eventName, p),
                p => Assert.Equal(average, long.Parse(p)),
                p => Assert.Equal(minimum, long.Parse(p)),
                p => Assert.Equal(maximum, long.Parse(p)),
                p => Assert.Equal(count, long.Parse(p)),
                p => Assert.Equal(ScriptHost.Version, p),
                p => Assert.True(DateTime.TryParse(p, out dt)));
        }

        [Theory]
        [MemberData(nameof(LinuxEventGeneratorTestData.GetDetailsEvents), MemberType = typeof(LinuxEventGeneratorTestData))]
        public void ParseDetailsEvents(string siteName, string functionName, string inputBindings, string outputBindings, string scriptType, bool isDisabled)
        {
            _generator.LogFunctionDetailsEvent(siteName, functionName, inputBindings, outputBindings, scriptType, isDisabled);

            string evt = _events[LinuxEventGenerator.FunctionsDetailsFileName].Single();

            Regex regex = new Regex(LinuxAppServiceEventGenerator.DetailsEventRegex);
            var match = regex.Match(evt);

            Assert.True(match.Success);
            Assert.Equal(7, match.Groups.Count);

            var groupMatches = match.Groups.Select(p => p.Value).Skip(1).ToArray();
            Assert.Collection(groupMatches,
                p => Assert.Equal(siteName, p),
                p => Assert.Equal(functionName, p),
                p => Assert.Equal(inputBindings, p),
                p => Assert.Equal(outputBindings, p),
                p => Assert.Equal(scriptType, p),
                p => Assert.Equal(isDisabled ? "1" : "0", p));
        }
    }
}
