// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics.Tracing;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Logging
{
    public class LinuxScriptLogger
    {
        public static readonly string EventTimestampFormat = "O";

        private const int MaxDetailsLength = 10000;
        private readonly Action<string> _writeEvent;
        private readonly string _containerName;
        private readonly string _stampName;
        private readonly string _tenantId;

        private static LinuxScriptLogger _generator = null;

        public LinuxScriptLogger()
        {
            _writeEvent = ConsoleWriter;
            IEnvironment environment = SystemEnvironment.Instance;
            _containerName = environment.GetEnvironmentVariable(EnvironmentSettingNames.ContainerName)?.ToUpperInvariant() ?? "T-CNAME";
            _stampName = environment.GetEnvironmentVariable(EnvironmentSettingNames.WebSiteHomeStampName)?.ToLowerInvariant() ?? "T-SNAME";
            _tenantId = environment.GetEnvironmentVariable(EnvironmentSettingNames.WebSiteStampDeploymentId)?.ToLowerInvariant() ?? "T-TID";
        }

        public static LinuxScriptLogger Instance => _generator ?? (_generator = new LinuxScriptLogger());

        private static void ConsoleWriter(string evt)
        {
            Console.WriteLine(evt);
        }

        public void Log(string message)
        {
            LogFunctionTraceEvent(LogLevel.Information, "sub", "app", "func", "evt", "source", "details", message,
                string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
                DateTime.UtcNow);
        }

        private void LogFunctionTraceEvent(LogLevel level, string subscriptionId, string appName, string functionName, string eventName, string source, string details, string summary, string exceptionType, string exceptionMessage, string functionInvocationId, string hostInstanceId, string activityId, string runtimeSiteName, string slotName, DateTime eventTimestamp)
        {
            string formattedEventTimeStamp = eventTimestamp.ToString(EventTimestampFormat);
            string hostVersion = ScriptHost.Version;
            details = details.Length > MaxDetailsLength ? details.Substring(0, MaxDetailsLength) : details;

            _writeEvent($"{ScriptConstants.LinuxLogEventStreamName} {(int)ToEventLevel(level)},{subscriptionId},{appName},{functionName},{eventName},{source},{NormalizeString(details)},{NormalizeString(summary)},{hostVersion},{formattedEventTimeStamp},{exceptionType},{NormalizeString(exceptionMessage)},{functionInvocationId},{hostInstanceId},{activityId},{_containerName},{_stampName},{_tenantId},{runtimeSiteName},{slotName}");
        }

        internal static string NormalizeString(string value)
        {
            // Need to remove newlines for csv output
            value = value.Replace(Environment.NewLine, " ");

            // Need to replace double quotes with single quotes as
            // our regex query looks at double quotes as delimeter for
            // individual column
            // TODO: Once the regex takes into account for quotes, we can
            // safely remove this
            value = value.Replace("\"", "'");

            // Wrap string literals in enclosing quotes
            // For string columns that may contain quotes and/or
            // our delimiter ',', before writing the value we
            // enclose in quotes. This allows us to define matching
            // groups based on quotes for these values.
            return $"\"{value}\"";
        }

        internal static EventLevel ToEventLevel(LogLevel level)
        {
            switch (level)
            {
                case LogLevel.Trace:
                case LogLevel.Debug:
                    return EventLevel.Verbose;
                case LogLevel.Information:
                    return EventLevel.Informational;
                case LogLevel.Warning:
                    return EventLevel.Warning;
                case LogLevel.Error:
                    return EventLevel.Error;
                case LogLevel.Critical:
                    return EventLevel.Critical;
                default:
                    return EventLevel.LogAlways;
            }
        }
    }
}