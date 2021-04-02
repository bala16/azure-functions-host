// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.LinuxSpecialization
{
    public class RunFromPackageDeploymentLockManager
    {
        private readonly IEnvironment _environment;
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger<RunFromPackageDeploymentLockManager> _logger;

        public RunFromPackageDeploymentLockManager(IEnvironment environment, ILoggerFactory loggerFactory)
        {
            _environment = environment;
            _loggerFactory = loggerFactory;
            _logger = loggerFactory.CreateLogger<RunFromPackageDeploymentLockManager>();
            _logger.LogInformation($"ctor {nameof(RunFromPackageDeploymentLockManager)}");
        }

        public async Task<IDisposable> TryAcquire()
        {
            try
            {
                _logger.LogInformation($"{nameof(RunFromPackageDeploymentLockManager)} {nameof(TryAcquire)} creating deployment lock");

                var logger = _loggerFactory.CreateLogger<RunFromPackageDeploymentLock>();
                var deploymentLock = new RunFromPackageDeploymentLock(_environment, logger);
                _logger.LogInformation($"{nameof(RunFromPackageDeploymentLockManager)} {nameof(TryAcquire)} initializing deployment lock");

                await deploymentLock.Initialize();
                _logger.LogInformation($"{nameof(RunFromPackageDeploymentLockManager)} {nameof(TryAcquire)} initialized deployment lock");

                return deploymentLock;
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, $"{nameof(RunFromPackageDeploymentLockManager)}.{nameof(TryAcquire)}");
                return null;
            }
        }
    }
}
