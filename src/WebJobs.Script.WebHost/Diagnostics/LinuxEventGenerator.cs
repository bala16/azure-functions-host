// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics.Tracing;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    public class LinuxEventGenerator
    {
        public static readonly string EventTimestampFormat = "MM/dd/yyyy hh:mm:ss.fff tt";
        public static readonly string FunctionsLogsFileName = "functionslogs.log";
        public static readonly string FunctionsMetricsFileName = "functionsmetrics.log";
        public static readonly string FunctionsDetailsFileName = "functionsdetails.log";

        internal static string NormalizeString(string value)
        {
            // need to remove newlines for csv output
            value = value.Replace(Environment.NewLine, " ");

            // Wrap string literals in enclosing quotes
            // For string columns that may contain quotes and/or
            // our delimiter ',', before writing the value we
            // enclose in quotes. This allows us to define matching
            // groups based on quotes for these values.
            return $"\"{value}\"";
        }

        /// <summary>
        /// Performs the same mapping from <see cref="LogLevel"/> to <see cref="EventLevel"/>
        /// that is performed for ETW event logging in <see cref="EtwEventGenerator"/>, so we
        /// have consistent log levels across platforms.
        /// </summary>
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
