// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Middleware
{
    public class WorkerValidationMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IEnvironment _environment;
        private readonly ILogger<WorkerValidationMiddleware> _logger;

        public WorkerValidationMiddleware(RequestDelegate next, IEnvironment environment, ILogger<WorkerValidationMiddleware> logger)
        {
            _next = next;
            _environment = environment;
            _logger = logger;
        }

        private bool IsWrongWorker(HttpRequest request)
        {
            var runtimeSiteName = _environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName);
            var siteDeploymentId = request.Headers[ScriptConstants.AntaresSiteDeploymentId];

            // Incoming is request is for a specific site.
            if (!string.IsNullOrEmpty(siteDeploymentId))
            {
                // There are 2 scenarios the request should be blocked
                // 1. current container hasn't been specialized yet.
                // (there could be a race between specialization and cold start request). So this middleware needs to be behind EnvironmentReadyCheckMiddleware
                // 2. current container is assigned to a different site.

                if (string.IsNullOrEmpty(runtimeSiteName) ||
                    !string.Equals(runtimeSiteName, siteDeploymentId, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("Request for site {0} sent to container specialized for site {1}",
                        siteDeploymentId, runtimeSiteName);
                    return true;
                }
            }

            return false;
        }

        public async Task Invoke(HttpContext context)
        {
            if (IsWrongWorker(context.Request))
            {
                _logger.LogInformation("Short circuiting request for " + context.Request.GetDisplayUrl());
                context.Response.Headers.Add("X-INVALIDATE-CACHE", "1");
            }
            else
            {
                _logger.LogInformation("Invoking next middleware for " + context.Request.GetDisplayUrl());
                await _next.Invoke(context);
            }
        }
    }
}
