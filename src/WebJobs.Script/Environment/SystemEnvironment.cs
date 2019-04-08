// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script
{
    public class SystemEnvironment : IEnvironment
    {
        private static readonly Lazy<SystemEnvironment> _instance = new Lazy<SystemEnvironment>(CreateInstance);

        private SystemEnvironment()
        {
        }

        public static SystemEnvironment Instance => _instance.Value;

        private static SystemEnvironment CreateInstance()
        {
            return new SystemEnvironment();
        }

        public string GetEnvironmentVariable(string name, ILogger logger = null)
        {
            logger?.LogInformation("GetEnvironmentVariable name " + name + " Contains " + EnvironmentSettingNames.EncodedSettingNames.Contains(name));

            var environmentVariable = Environment.GetEnvironmentVariable(name);

            logger?.LogInformation("GetEnvironmentVariable name " + name + " Contains " + EnvironmentSettingNames.EncodedSettingNames.Contains(name) + " environmentVariable = " + environmentVariable);

            var isEnvironmentVariableEncrypted = this.IsEnvironmentVariableEncrypted(name, logger);

            logger?.LogInformation("GetEnvironmentVariable name " + name + " Contains " + EnvironmentSettingNames.EncodedSettingNames.Contains(name) + " environmentVariable = " + environmentVariable + " isEnvironmentVariableEncrypted = " + isEnvironmentVariableEncrypted);

            return isEnvironmentVariableEncrypted ? Utility.DecodeEnvironment(environmentVariable, logger) : environmentVariable;
        }

        public void SetEnvironmentVariable(string name, string value)
        {
            Environment.SetEnvironmentVariable(name, value);
        }
    }
}
