﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authorization.Policies;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

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

        public InstanceController(IEnvironment environment, IInstanceManager instanceManager, ILoggerFactory loggerFactory)
        {
            _environment = environment;
            _instanceManager = instanceManager;
            _logger = loggerFactory.CreateLogger<InstanceController>();
        }

        [HttpPost]
        [Route("admin/instance/assign")]
        [Authorize(Policy = PolicyNames.AdminAuthLevel)]
        public async Task<IActionResult> Assign([FromBody] EncryptedHostAssignmentContext encryptedAssignmentContext)
        {
            _logger.LogDebug($"Starting container assignment for host : {Request?.Host}. ContextLength is: {encryptedAssignmentContext.EncryptedContext?.Length}");
            var containerKey = _environment.GetEnvironmentVariable(EnvironmentSettingNames.ContainerEncryptionKey);
            var assignmentContext = encryptedAssignmentContext.IsWarmup
                ? null
                : encryptedAssignmentContext.Decrypt(containerKey);

            // before starting the assignment we want to perform as much
            // up front validation on the context as possible
            string error = await _instanceManager.ValidateContext(assignmentContext, encryptedAssignmentContext.IsWarmup);
            if (error != null)
            {
                return StatusCode(StatusCodes.Status400BadRequest, error);
            }

            _logger.LogError($"encryptedAssignmentContext.IsWarmup={encryptedAssignmentContext.IsWarmup}");

            if (!encryptedAssignmentContext.IsWarmup)
            {
                _logger.LogInformation(JsonConvert.SerializeObject(assignmentContext, Formatting.Indented));
            }

            // Wait for Sidecar specialization to complete before returning ok.
            // This shouldn't take too long so ok to do this sequentially.
            error = await _instanceManager.SpecializeMSISidecar(assignmentContext, encryptedAssignmentContext.IsWarmup);
            if (error != null)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, error);
            }

            var result = _instanceManager.StartAssignment(assignmentContext, encryptedAssignmentContext.IsWarmup);

            return result || encryptedAssignmentContext.IsWarmup
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
    }
}
