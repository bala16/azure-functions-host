// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
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

        [HttpGet]
        [Route("admin/instance/publish-event")]
        public async Task<IActionResult> PublishEvent([FromQuery] int eventType, [FromQuery] bool done)
        {
            ContainerHealthEventType evt = ContainerHealthEventType.Informational;
            switch (eventType)
            {
                case 1:
                    evt = ContainerHealthEventType.Informational;
                    break;
                case 2:
                    evt = ContainerHealthEventType.Warning;
                    break;
                case 3:
                    evt = ContainerHealthEventType.Fatal;
                    break;
            }

            string details = "Specialization complete";
            if (!done)
            {
                details = "Some event";
            }

            await _meshServiceClient.NotifyHealthEvent(evt, GetType(), details);
            return Ok(evt.ToString() + " " + details);
        }

        [HttpGet]
        [Route("admin/instance/add-mount")]
        public async Task<IActionResult> AddMount([FromQuery] int method)
        {
            switch (method)
            {
                case 1:
                    await _meshServiceClient.MountFuse("zip", "/tmp/zip", "/etc");
                    return Ok("zip");
                case 2:
                    await _meshServiceClient.MountFuse("squashfs", "/tmp/squash", "/etc");
                    return Ok("squashfs");
                case 3:
                    var environmentVariable = _environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureFilesConnectionString);
                    await _meshServiceClient.MountCifs(environmentVariable, "te1", "/etc");
                    return Ok("cifs1");
                case 4:
                    var connectionString = _environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureFilesConnectionString);
                    await _meshServiceClient.MountCifs(connectionString, "te1", "/abcd");
                    return Ok("cifs2");
            }

            return Ok("unknown");
        }

        [HttpGet]
        [Route("admin/instance/exit")]
        public string Exit()
        {
            Process.GetCurrentProcess().Kill();
            return DateTime.Now.ToString(CultureInfo.InvariantCulture);
        }

        [HttpGet]
        [Route("admin/instance/list")]
        public string ListFiles([FromQuery] string path)
        {
            _logger.LogInformation($"Listing {path}");
            var stringBuilder = new StringBuilder();
            foreach (var f in Directory.EnumerateFileSystemEntries($"/{path}"))
            {
                stringBuilder.AppendLine(f);
            }

            return stringBuilder.ToString();
        }
    }
}
