// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Azure.WebJobs.Script.WebHost.Security;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authorization.Policies;

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

        public InstanceController(IEnvironment environment, IInstanceManager instanceManager)
        {
            _environment = environment;
            _instanceManager = instanceManager;
        }

        [HttpPost]
        [Route("admin/instance/assign")]
        [Authorize(Policy = PolicyNames.AdminAuthLevel)]
        public async Task<IActionResult> Assign([FromBody] EncryptedHostAssignmentContext encryptedAssignmentContext)
        {
            var containerKey = _environment.GetEnvironmentVariable(EnvironmentSettingNames.ContainerEncryptionKey);
            var assignmentContext = encryptedAssignmentContext.Decrypt(containerKey);

            // before starting the assignment we want to perform as much
            // up front validation on the context as possible
            string error = await _instanceManager.ValidateContext(assignmentContext);
            if (error != null)
            {
                return StatusCode(StatusCodes.Status400BadRequest, error);
            }

            var result = _instanceManager.StartAssignment(assignmentContext);

            return result
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
        [Route("admin/instance/authcontainer")]
        public IActionResult AuthenticateContainer(bool useToken)
        {
            var authresult = DoAuth(useToken).Result;
            Console.WriteLine("authresult =" + authresult);
            return StatusCode((int)authresult, "AuthResult");
        }

        internal static HttpRequestMessage BuildSetTriggersRequest()
        {
            var protocol = "https";
            // On private stamps with no ssl certificate use http instead.
            if (Environment.GetEnvironmentVariable(EnvironmentSettingNames.SkipSslValidation) == "1")
            {
                protocol = "http";
            }

            Console.WriteLine("=============protocol=" + protocol);

            var hostname = Environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteHostName);
            Console.WriteLine("=============hostname1=" + hostname);
            // Linux Dedicated on AppService doesn't have WEBSITE_HOSTNAME
            hostname = string.IsNullOrWhiteSpace(hostname)
                ? $"{Environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName)}.azurewebsites.net"
                : hostname;
            Console.WriteLine("=============hostname2=" + hostname);

            var url = $"{protocol}://{hostname}/operations/authenticatesfcontainer";

            Console.WriteLine("=============url=" + url);
            return new HttpRequestMessage(HttpMethod.Post, url);
        }

        private static async Task<HttpStatusCode> DoAuth(bool useToken)
        {
            var token = useToken ? SimpleWebTokenHelper.CreateToken(DateTime.UtcNow.AddMinutes(5)) : string.Empty;

            Console.WriteLine("=============token=" + token);

            using (var httpClient = new HttpClient())
            {
                using (var httpRequestMessage = BuildSetTriggersRequest())
                {
                    httpRequestMessage.Headers.Add("User-Agent", "Mozilla/5.0");
                    httpRequestMessage.Headers.Add("x-ms-site-restricted-token", token);

                    var response = await httpClient.SendAsync(httpRequestMessage);
                    return response.StatusCode;
                }
            }
        }
    }
}
