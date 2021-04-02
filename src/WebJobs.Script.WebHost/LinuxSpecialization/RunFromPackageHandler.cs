// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost.LinuxSpecialization
{
    public class RunFromPackageHandler : RunFromPackageHandlerBase
    {
        private const string CurrentDeploymentMarkerFile = "marker";

        private readonly IEnvironment _environment;
        private readonly RunFromPackageDeploymentLockManager _runFromPackageDeploymentLockManager;
        private readonly ILogger<RunFromPackageHandler> _logger;

        public RunFromPackageHandler(IEnvironment environment, RunFromPackageDeploymentLockManager runFromPackageDeploymentLockManager,
            IOptionsFactory<ScriptApplicationHostOptions> optionsFactory, HttpClient client,
            IMeshServiceClient meshServiceClient, IMetricsLogger metricsLogger,
            ILogger<RunFromPackageHandler> logger) : base(environment, optionsFactory, client,
            meshServiceClient, metricsLogger, logger)
        {
            _environment = environment;
            _runFromPackageDeploymentLockManager = runFromPackageDeploymentLockManager;
            _logger = logger;
            _logger.LogInformation($"ctor {nameof(RunFromPackageHandler)}");
        }

        public string GetCurrentDeploymentMarkerFilePath()
        {
            var deploymentMetadataFolderPath = _environment.GetDeploymentMetadataFolderPath();
            _logger.LogInformation($"deploymentMetadataFolderPath = {deploymentMetadataFolderPath}");
            var currentDeploymentMarkerFilePath = Path.Combine(deploymentMetadataFolderPath, CurrentDeploymentMarkerFile);
            _logger.LogInformation($"{nameof(RunFromPackageHandler)} {nameof(GetCurrentDeploymentMarkerFilePath)} = {currentDeploymentMarkerFilePath}");
            return currentDeploymentMarkerFilePath;
        }

        private bool IsValidFunctionsFolder(string path)
        {
            var isValidFunctionsFolder = Directory.Exists(Path.Combine(path, ScriptConstants.HostMetadataFileName));
            _logger.LogInformation($"{nameof(RunFromPackageHandler)} {nameof(IsValidFunctionsFolder)} = {isValidFunctionsFolder}");
            return isValidFunctionsFolder;
        }

        private bool IsEmpty(string path)
        {
            var isEmpty = Directory.EnumerateFileSystemEntries(path).Any();
            _logger.LogInformation($"{nameof(RunFromPackageHandler)} {nameof(IsEmpty)} = {isEmpty}");
            return isEmpty;
        }

        // The following scenarios will trigger a refresh
        // 1. /home/site/wwwroot is empty
        // 2. /home/site/wwwroot has invalid data
        // 3. /home/site/wwwroot has valid contents but contents of /home/data/deploymentmetadata/{siteSlotShareName}/current
        // is different from configured Run-From-Pkg url (There was a recent deployment)
        private async Task<bool> HasContentChanged(RunFromPackageContext pkgContext)
        {
            try
            {
                var markerFilePath = GetCurrentDeploymentMarkerFilePath();

                _logger.LogInformation($"{nameof(RunFromPackageHandler)} {nameof(markerFilePath)} = {markerFilePath}");

                if (!File.Exists(markerFilePath))
                {
                    // If we cant find the marker, we err on the side of redeploying to avoid leaving the app running with old content
                    _logger.LogWarning("No deployment marker file found.");
                    return true;
                }

                // SCM_RUN_FROM_PACKAGE will never change. so this check will not work correctly. But powershell will not be using SCM_..
                var lastKnownDeploymentUrl = await File.ReadAllTextAsync(markerFilePath);
                if (string.IsNullOrWhiteSpace(lastKnownDeploymentUrl))
                {
                    _logger.LogWarning("Couldn't read last known deployment url.");
                    return true;
                }

                var contentChanged = !string.Equals(lastKnownDeploymentUrl, pkgContext.Url, StringComparison.OrdinalIgnoreCase);
                _logger.LogInformation($"Has RunFromPackage url changed? {contentChanged}");
                return contentChanged;
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, nameof(HasContentChanged));
                return true;
            }
        }

        private async Task CommitDeployment()
        {
            try
            {
                _logger.LogInformation($"Committing deployment");
                var markerFilePath = GetCurrentDeploymentMarkerFilePath();

                _logger.LogInformation($"Writing to deployment markerfile = {markerFilePath}");

                await File.WriteAllTextAsync(markerFilePath,
                    _environment.GetEnvironmentVariable(EnvironmentSettingNames.ContainerName));

                _logger.LogInformation($"Wrote to deployment markerfile = {markerFilePath}");
            }
            catch (Exception e)
            {
                // Not fatal
                _logger.LogWarning(e, nameof(CommitDeployment));
            }
        }

        // Uses local disk as backing store
        public override async Task<bool> DeployToLocalDisk(HostAssignmentContext assignmentContext, RunFromPackageContext pkgContext)
        {
            _logger.LogInformation($"Start {nameof(DeployToLocalDisk)}");
            return await ApplyBlobPackageContext(assignmentContext, pkgContext);
        }

        // Uses Azure Files as backing store. Doesn't support WEBSITE_RUN_FROM_PACKAGE=1 or SCM_RUN_FROM_PACKAGE
        public override async Task<bool> DeployToAzureFiles(HostAssignmentContext assignmentContext, RunFromPackageContext runFromPackageContext)
        {
            _logger.LogInformation($"Start {nameof(DeployToAzureFiles)}");

            try
            {
                if (!assignmentContext.IsAzureFilesContentShareConfigured())
                {
                    _logger.LogDebug(
                        $"Skipping {nameof(RunFromPackageHandler)} deployment since {nameof(EnvironmentSettingNames.AzureFilesConnectionString)} is not configured. App will fallback to local deployment");
                    return false;
                }

                if (runFromPackageContext.IsScmRunFromPackage())
                {
                    _logger.LogDebug(
                        $"Skipping {nameof(RunFromPackageHandler)} deployment since {nameof(EnvironmentSettingNames.ScmRunFromPackage)} is not supported. App will fallback to local deployment");
                    return false;
                }

                var contentRootFolder = _environment.GetContentRootFolder();
                _logger.LogInformation($"{nameof(contentRootFolder)} = {contentRootFolder}");

                var shouldDeploy = false;

                if (IsEmpty(contentRootFolder))
                {
                    shouldDeploy = true;
                    _logger.LogInformation("Triggering deployment since ContentRoot was empty.");
                }

                if (!shouldDeploy)
                {
                    if (!IsValidFunctionsFolder(contentRootFolder))
                    {
                        shouldDeploy = true;
                        _logger.LogInformation("Triggering deployment since ContentRoot has invalid contents.");
                    }
                }

                if (!shouldDeploy)
                {
                    // More expensive check to see if there has been a deployment.
                    shouldDeploy = await HasContentChanged(runFromPackageContext);
                    _logger.LogInformation(
                        $"Triggering deployment since value of {nameof(EnvironmentSettingNames.AzureWebsiteRunFromPackage)} has changed.");
                }

                if (!shouldDeploy)
                {
                    // The contents are already in place. Nothing more to do here.
                    _logger.LogInformation(
                        $"Skipping deployment since {nameof(EnvironmentSettingNames.AzureFilesContentShare)} has the latest contents.");
                    return true;
                }

                _logger.LogInformation($"Acquiring deployment lock");

                var deploymentLock = await _runFromPackageDeploymentLockManager.TryAcquire();
                if (deploymentLock == null)
                {
                    _logger.LogWarning($"Failed to acquire deployment lock. Using local disk for deployment.");
                    // We couldn't acquire a lock for some reason. Fall back to using Local disk as fallback.
                    return false;
                }

                using (deploymentLock)
                {
                    _logger.LogInformation($"Starting {nameof(ApplyBlobPackageContext)}");
                    var success = await ApplyBlobPackageContext(assignmentContext, runFromPackageContext);
                    if (success)
                    {
                        _logger.LogInformation($"Committing deployment {nameof(DeployToAzureFiles)}");
                        await CommitDeployment();
                        _logger.LogInformation($"Committed deployment {nameof(DeployToAzureFiles)}");
                    }

                    return success;
                }
            }
            catch (Exception e)
            {
                _logger.LogWarning(nameof(DeployToAzureFiles), e);
                return false;
            }
        }
    }
}
