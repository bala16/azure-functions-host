// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public class LinuxContainerInitializationService : ILinuxContainerInitializationService
    {
        private readonly ScriptSettingsManager _settingsManager;
        private readonly IInstanceManager _instanceManager;
        private readonly IEncyptedHostAssignmentContextReader _encyptedHostAssignmentContextReader;
        private readonly ILogger _logger;

        public LinuxContainerInitializationService(ScriptSettingsManager settingsManager, IInstanceManager instanceManager, IEncyptedHostAssignmentContextReader encyptedHostAssignmentContextReader, ILoggerFactory loggerFactory)
        {
            _settingsManager = settingsManager;
            _instanceManager = instanceManager;
            _encyptedHostAssignmentContextReader = encyptedHostAssignmentContextReader;
            _logger = loggerFactory.CreateLogger(LogCategories.Startup);
        }

        public async Task Run(CancellationToken cancellationToken)
        {
            if (_settingsManager.IsLinuxContainerEnvironment)
            {
                await InitializeAssignmentContext(cancellationToken);
            }
        }

        private async Task InitializeAssignmentContext(CancellationToken cancellationToken)
        {
            var webKey = Environment.GetEnvironmentVariable(EnvironmentSettingNames.WebSiteAuthEncryptionKey);
            var conKey = Environment.GetEnvironmentVariable(EnvironmentSettingNames.ContainerEncryptionKey);

            _logger.LogInformation("AAA webkey " + webKey);
            _logger.LogInformation("AAA conKey " + conKey);

            var startContext = _settingsManager.GetSetting(EnvironmentSettingNames.ContainerStartContext);

            // Container start context is not available directly
            if (string.IsNullOrEmpty(startContext))
            {
                _logger.LogInformation("AssignmentContext not available in ContainerStartContext");
                // Check if the context is available in blob
                var sasUri = _settingsManager.GetSetting(EnvironmentSettingNames.ContainerStartContextSasUri);

                if (!string.IsNullOrEmpty(sasUri))
                {
                    _logger.LogInformation("AssignmentContext ContainerStartContextSasUri available");
                    startContext = await GetAssignmentContextFromSasUri(sasUri, cancellationToken);
                }
            }

            if (!string.IsNullOrEmpty(startContext))
            {
                _logger.LogInformation("Assigning HostAssignmentContext.");

                var encryptedAssignmentContext = JsonConvert.DeserializeObject<EncryptedHostAssignmentContext>(startContext);
                var containerKey = _settingsManager.GetSetting(EnvironmentSettingNames.ContainerEncryptionKey);
                var assignmentContext = encryptedAssignmentContext.Decrypt(containerKey);
                if (_instanceManager.StartAssignment(assignmentContext))
                {
                    _logger.LogInformation("Assign HostAssignmentContext success");
                }
                else
                {
                    _logger.LogError("Assign HostAssignmentContext failed");
                }
            }
            else
            {
                _logger.LogInformation("Waiting for /assign to receive AssignmentContext");
            }
        }

        private async Task<string> GetAssignmentContextFromSasUri(string sasUri, CancellationToken cancellationToken)
        {
            try
            {
                return await _encyptedHostAssignmentContextReader.Read(sasUri, cancellationToken);
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"Error calling {nameof(GetAssignmentContextFromSasUri)}");
            }

            return string.Empty;
        }
    }
}
