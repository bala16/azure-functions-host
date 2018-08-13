// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    internal class LinuxAppServiceEventGenerator : LinuxEventGenerator, IEventGenerator
    {
        private readonly Action<string, string> _writeEvent;
        private readonly string _functionsLogsMountPath;

        public LinuxAppServiceEventGenerator(Action<string, string> writeEvent = null)
        {
            _writeEvent = writeEvent ?? FileLogWriter;
            _functionsLogsMountPath = Environment.GetEnvironmentVariable(EnvironmentSettingNames.FunctionsLogsMountPath);
        }

        public static string TraceEventRegex { get; } = $"(?<Level>[0-6]),(?<SubscriptionId>[^,]*),(?<AppName>[^,]*),(?<FunctionName>[^,]*),(?<EventName>[^,]*),(?<Source>[^,]*),\"(?<Details>.*)\",\"(?<Summary>.*)\",(?<HostVersion>[^,]*),(?<EventTimestamp>[^,]+),(?<ExceptionType>[^,]*),\"(?<ExceptionMessage>.*)\",(?<FunctionInvocationId>[^,]*),(?<HostInstanceId>[^,]*),(?<ActivityId>[^,\"]*)";

        public static string MetricEventRegex { get; } = $"(?<SubscriptionId>[^,]*),(?<AppName>[^,]*),(?<FunctionName>[^,]*),(?<EventName>[^,]*),(?<Average>\\d*),(?<Min>\\d*),(?<Max>\\d*),(?<Count>\\d*),(?<HostVersion>[^,]*),(?<EventTimestamp>[^,\"]+)";

        public static string DetailsEventRegex { get; } = $"(?<AppName>[^,]*),(?<FunctionName>[^,]*),\"(?<InputBindings>.*)\",\"(?<OutputBindings>.*)\",(?<ScriptType>[^,]*),(?<IsDisabled>[0|1])";

        public void LogFunctionTraceEvent(LogLevel level, string subscriptionId, string appName, string functionName, string eventName,
            string source, string details, string summary, string exceptionType, string exceptionMessage,
            string functionInvocationId, string hostInstanceId, string activityId)
        {
            string eventTimestamp = DateTime.UtcNow.ToString(EventTimestampFormat);
            string hostVersion = ScriptHost.Version;
            FunctionsSystemLogsEventSource.Instance.SetActivityId(activityId);

            _writeEvent(FunctionsLogsFileName, $"{(int)ToEventLevel(level)},{subscriptionId},{appName},{functionName},{eventName},{source},{NormalizeString(details)},{NormalizeString(summary)},{hostVersion},{eventTimestamp},{exceptionType},{NormalizeString(exceptionMessage)},{functionInvocationId},{hostInstanceId},{activityId}");
        }

        public void LogFunctionMetricEvent(string subscriptionId, string appName, string functionName, string eventName, long average,
            long minimum, long maximum, long count, DateTime eventTimestamp)
        {
            string hostVersion = ScriptHost.Version;

            _writeEvent(FunctionsMetricsFileName, $"{subscriptionId},{appName},{functionName},{eventName},{average},{minimum},{maximum},{count},{hostVersion},{eventTimestamp.ToString(EventTimestampFormat)}");

            //todo remove
            _writeEvent(FunctionsDetailsFileName, $"siteName,{functionName},NormalizeString(inputBindings),NormalizeString(outputBindings),scriptType,{1}");
        }

        public void LogFunctionDetailsEvent(string siteName, string functionName, string inputBindings, string outputBindings,
            string scriptType, bool isDisabled)
        {
            _writeEvent(FunctionsDetailsFileName, $"{siteName},{functionName},{NormalizeString(inputBindings)},{NormalizeString(outputBindings)},{scriptType},{(isDisabled ? 1 : 0)}");
        }

        public void LogFunctionExecutionAggregateEvent(string siteName, string functionName, long executionTimeInMs,
            long functionStartedCount, long functionCompletedCount, long functionFailedCount)
        {
            throw new NotImplementedException();
        }

        public void LogFunctionExecutionEvent(string executionId, string siteName, int concurrency, string functionName,
            string invocationId, string executionStage, long executionTimeSpan, bool success)
        {
            throw new NotImplementedException();
        }

        private void FileLogWriter(string fileName, string evt)
        {
            var logFilePath = Path.Combine(_functionsLogsMountPath, fileName);

            using (var writer = File.AppendText(logFilePath))
            {
                writer.WriteLine(evt);
            }
        }
    }
}
