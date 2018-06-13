// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.WebHost.ContainerManagement
{
    public class LinuxContainerInitializationHostService : IHostedService
    {
        private readonly ScriptSettingsManager _settingsManager;
        private readonly IInstanceManager _instanceManager;
        private readonly ILogger _logger;
        private CancellationToken _cancellationToken;

        public LinuxContainerInitializationHostService(ScriptSettingsManager settingsManager, IInstanceManager instanceManager, ILoggerFactory loggerFactory)
        {
            _settingsManager = settingsManager;
            _instanceManager = instanceManager;
            _logger = loggerFactory.CreateLogger(ScriptConstants.LogCategoryHostGeneral);
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Initializing LinuxContainerInitializationService.");
            _cancellationToken = cancellationToken;
            await Run();
        }

        private async Task Run()
        {
            if (_settingsManager.IsLinuxContainerEnvironment)
            {
                await InitializeAssignmentContext();
            }
        }

        private async Task InitializeAssignmentContext()
        {
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
                    startContext = await GetAssignmentContextFromSasUri(sasUri);
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

        private async Task<string> GetAssignmentContextFromSasUri(string sasUri)
        {
            try
            {
                return await Read(sasUri);
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"Error calling {nameof(GetAssignmentContextFromSasUri)}");
            }

            return string.Empty;
        }

        // virtual for unit testing
        public virtual async Task<string> Read(string uri)
        {
            var cloudBlockBlob = new CloudBlockBlob(new Uri(uri));
            if (await cloudBlockBlob.ExistsAsync(null, null, _cancellationToken))
            {
                return await cloudBlockBlob.DownloadTextAsync(null, null, null, null, _cancellationToken);
            }

            return string.Empty;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Stopping LinuxContainerInitializationService.");
            return Task.CompletedTask;
        }
    }
}
