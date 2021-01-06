// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authorization.Policies;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Controllers
{
    /// <summary>
    /// Controller responsible for instance operations that are orthogonal to the script host.
    /// An instance is an unassigned generic container running with the runtime in standby mode.
    /// These APIs are used by the AppService Controller to validate standby instance status and info.
    /// </summary>
    public class InstanceController : Controller
    {
        private readonly IEnvironment _environment;
        private readonly IInstanceManager _instanceManager;
        private readonly ILogger _logger;
        private readonly StartupContextProvider _startupContextProvider;
        private readonly IMeshServiceClient _meshServiceClient;

        public InstanceController(IEnvironment environment, IInstanceManager instanceManager, ILoggerFactory loggerFactory, StartupContextProvider startupContextProvider, IMeshServiceClient meshServiceClient)
        {
            _environment = environment;
            _instanceManager = instanceManager;
            _logger = loggerFactory.CreateLogger<InstanceController>();
            _startupContextProvider = startupContextProvider;
            _meshServiceClient = meshServiceClient;
        }

        [HttpPost]
        [Route("admin/instance/assign")]
        [Authorize(Policy = PolicyNames.AdminAuthLevel)]
        public async Task<IActionResult> Assign([FromBody] EncryptedHostAssignmentContext encryptedAssignmentContext)
        {
            _logger.LogDebug($"Starting container assignment for host : {Request?.Host}. ContextLength is: {encryptedAssignmentContext.EncryptedContext?.Length}");

            var assignmentContext = _startupContextProvider.SetContext(encryptedAssignmentContext);

            // before starting the assignment we want to perform as much
            // up front validation on the context as possible
            string error = await _instanceManager.ValidateContext(assignmentContext);
            if (error != null)
            {
                return StatusCode(StatusCodes.Status400BadRequest, error);
            }

            // Wait for Sidecar specialization to complete before returning ok.
            // This shouldn't take too long so ok to do this sequentially.
            error = await _instanceManager.SpecializeMSISidecar(assignmentContext);
            if (error != null)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, error);
            }

            var succeeded = _instanceManager.StartAssignment(assignmentContext);

            return succeeded
                ? Accepted()
                : StatusCode(StatusCodes.Status409Conflict, "Instance already assigned");
        }

        [HttpGet]
        [Route("admin/instance/info")]
        [Authorize(Policy = PolicyNames.AdminAuthLevel)]
        public IActionResult GetInstanceInfo()
        {
            return Ok(_instanceManager.GetInstanceInfo());
        }

        [HttpGet]
        [Route("admin/instance/mount")]
        public async Task<IActionResult> Mount([FromQuery] string shareName, [FromQuery] string mountPath,
            [FromQuery] bool decode)
        {
            string storageAccountName = _environment.GetEnvironmentVariable("StorageAccount");
            string storageKey = _environment.GetEnvironmentVariable("StorageKey");

            if (string.IsNullOrWhiteSpace(storageAccountName))
            {
                return Conflict("Empty storage account");
            }

            if (string.IsNullOrWhiteSpace(storageKey))
            {
                return Conflict("empty storage key");
            }

            _logger.LogInformation($"ShareName = {shareName}");
            _logger.LogInformation($"MountPath = {mountPath}");
            _logger.LogInformation($"Decode = {decode}");

            if (decode)
            {
                mountPath = HttpUtility.UrlDecode(mountPath);
            }

            _logger.LogInformation($"Decoded MountPath = {mountPath}");
            var azureStorageInfoValue = new AzureStorageInfoValue(Guid.NewGuid().ToString("N"), AzureStorageType.AzureFiles, storageAccountName,
                shareName, storageKey, mountPath);
            var result = await MountStorageAccount(azureStorageInfoValue);
            _logger.LogInformation($"Mount result = {result}");

            return Ok($"{shareName} {result}");
        }

        [HttpGet]
        [Route("admin/instance/list")]
        public string List([FromQuery] string mountPath, [FromQuery] bool decode)
        {
            if (decode)
            {
                mountPath = HttpUtility.UrlDecode(mountPath);
            }

            var stringBuilder = new StringBuilder();
            foreach (var file in Directory.EnumerateFiles(mountPath))
            {
                stringBuilder.Append(file);
            }

            return stringBuilder.ToString();
        }

        private async Task<bool> MountStorageAccount(AzureStorageInfoValue storageInfoValue)
        {
            var storageConnectionString =
                Utility.BuildStorageConnectionString(storageInfoValue.AccountName, storageInfoValue.AccessKey, _environment.GetStorageSuffix());

            if (!await _meshServiceClient.MountCifs(storageConnectionString, storageInfoValue.ShareName, storageInfoValue.MountPath))
            {
                throw new Exception($"Failed to mount BYOS fileshare {storageInfoValue.ShareName}");
            }

            return true;
        }

        [HttpPost]
        [Route("admin/instance/disable")]
        [Authorize(Policy = PolicyNames.AdminAuthLevel)]
        public async Task<IActionResult> Disable([FromServices] IScriptHostManager hostManager)
        {
            _logger.LogDebug("Disabling container");
            // Mark the container disabled. We check for this on host restart
            await Utility.MarkContainerDisabled(_logger);
            var tIgnore = Task.Run(() => hostManager.RestartHostAsync());
            return Ok();
        }

        [HttpGet]
        [Route("admin/instance/http-health")]
        public IActionResult GetHttpHealthStatus()
        {
            // Reaching here implies that http health of the container is ok.
            return Ok();
        }
    }
}
