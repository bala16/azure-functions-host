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
        }

        public string GetCurrentDeploymentMarkerFilePath()
        {
            return Path.Combine(_environment.GetDeploymentMetadataFolderPath(), CurrentDeploymentMarkerFile);
        }

        private bool IsValidFunctionsFolder(string path)
        {
            return Directory.Exists(Path.Combine(path, ScriptConstants.HostMetadataFileName));
        }

        private bool IsEmpty(string path)
        {
            return Directory.EnumerateFileSystemEntries(path).Any();
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
                if (!File.Exists(markerFilePath))
                {
                    // If we cant find the marker, we err on the side of redeploying to avoid leaving the app running with old content
                    return true;
                }

                // SCM_RUN_FROM_PACKAGE will never change. so this check will not work correctly. But powershell will not be using SCM_..
                var lastKnownDeploymentUrl = await File.ReadAllTextAsync(markerFilePath);
                if (string.IsNullOrWhiteSpace(lastKnownDeploymentUrl))
                {
                    return true;
                }

                var contentChanged = !string.Equals(lastKnownDeploymentUrl, pkgContext.Url, StringComparison.OrdinalIgnoreCase);
                _logger.LogInformation($"Has RunFromPackage url change? {contentChanged}");
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
                var markerFilePath = GetCurrentDeploymentMarkerFilePath();
                await File.WriteAllTextAsync(markerFilePath,
                    _environment.GetEnvironmentVariable(EnvironmentSettingNames.ContainerName));
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
            return await ApplyBlobPackageContext(assignmentContext, pkgContext);
        }

        // Uses Azure Files as backing store. Doesn't support WEBSITE_RUN_FROM_PACKAGE=1 or SCM_RUN_FROM_PACKAGE
        public override async Task<bool> DeployToAzureFiles(HostAssignmentContext assignmentContext, RunFromPackageContext runFromPackageContext)
        {
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

                var deploymentLock = await _runFromPackageDeploymentLockManager.TryAcquire();
                if (deploymentLock == null)
                {
                    // We couldn't acquire a lock for some reason. Fall back to using Local disk as fallback.
                    return false;
                }

                using (deploymentLock)
                {
                    var success = await ApplyBlobPackageContext(assignmentContext, runFromPackageContext);
                    if (success)
                    {
                        await CommitDeployment();
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
