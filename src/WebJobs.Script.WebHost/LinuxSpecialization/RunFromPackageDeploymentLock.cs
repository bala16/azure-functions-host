// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.LinuxSpecialization
{
    public class RunFromPackageDeploymentLock : IDisposable
    {
        private const string DeploymentLockFile = "lockfile";
        private const string LockFileTypeCurrent = "current";
        private const string LockFileTypePending = "pending";

        private readonly string _lockFilePath;
        private readonly IEnvironment _environment;
        private readonly ILogger<RunFromPackageDeploymentLock> _logger;
        private readonly int _delaySeconds;

        public RunFromPackageDeploymentLock(IEnvironment environment, ILogger<RunFromPackageDeploymentLock> logger, int delaySeconds = 5)
        {
            _lockFilePath = GetDeploymentLockFilepath();
            logger.LogInformation($"{nameof(RunFromPackageDeploymentLock)} Setting {nameof(_lockFilePath)} to {_lockFilePath}");
            _environment = environment;
            _logger = logger;
            _delaySeconds = delaySeconds;
        }

        private string GetDeploymentLockFilepath()
        {
            return Path.Combine(_environment.GetDeploymentMetadataFolderPath(), DeploymentLockFile);
        }

        public async Task Initialize()
        {
            await CreateDeploymentLockFile();
        }

        private async Task CreateDeploymentLockFile()
        {
            _logger.LogInformation($"{nameof(RunFromPackageDeploymentLock)} Deleting pending {nameof(CreateDeploymentLockFile)}");

            // It is possible (but rare) there are multiple instances trying to specialize at the same time.
            // Code here favors refreshing contents instead of reusing contents from a concurrent deployment.
            await DeleteDeploymentLockFile(_delaySeconds, LockFileTypePending);

            _logger.LogInformation($"{nameof(RunFromPackageDeploymentLock)} Deleted pending {nameof(CreateDeploymentLockFile)}");

            var currentContainerName = _environment.GetEnvironmentVariable(EnvironmentSettingNames.ContainerName);

            _logger.LogInformation($"{nameof(RunFromPackageDeploymentLock)} Writing {currentContainerName} to {_lockFilePath}");

            await File.WriteAllTextAsync(_lockFilePath, currentContainerName);
        }

        private async Task DeleteDeploymentLockFile(int delaySeconds, string lockFileType)
        {
            if (File.Exists(_lockFilePath))
            {
                var lastWriteTime = File.GetLastWriteTime(_lockFilePath);
                var deploymentLockFileContents = await File.ReadAllTextAsync(_lockFilePath);
                _logger.LogInformation(
                    $"Deleting {lockFileType} {nameof(DeploymentLockFile)} created at {lastWriteTime} with contents {deploymentLockFileContents}");

                await Task.Delay(TimeSpan.FromSeconds(delaySeconds));

                File.Delete(_lockFilePath);
                _logger.LogInformation($"Successfully deleted {lockFileType} {nameof(DeploymentLockFile)}");
            }
            else
            {
                _logger.LogWarning($"Skipping delete of {lockFileType} {nameof(DeploymentLockFile)}");
            }
        }

        public void Dispose()
        {
            try
            {
                _logger.LogInformation($"{nameof(RunFromPackageDeploymentLock)} deleting current lock file");

                // Can't use IAsyncDisposable yet. Doing a blocking wait instead.
                DeleteDeploymentLockFile(0, LockFileTypeCurrent).Wait();

                _logger.LogInformation($"{nameof(RunFromPackageDeploymentLock)} deleted current lock file");
            }
            catch (Exception e)
            {
                // Failure is not fatal. Lock file will be cleaned up by next container assigned to the same site
                _logger.LogWarning(e, $"{nameof(RunFromPackageDeploymentLock)}.{nameof(Dispose)}");
            }
        }
    }
}