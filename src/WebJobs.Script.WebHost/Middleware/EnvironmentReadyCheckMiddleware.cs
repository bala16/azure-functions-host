// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Middleware
{
    /// <summary>
    /// Middleware registered early in the request pipeline to check host
    /// environment and delay requests as necessary.
    /// </summary>
    public partial class EnvironmentReadyCheckMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<EnvironmentReadyCheckMiddleware> _logger;

        public EnvironmentReadyCheckMiddleware(RequestDelegate next, ILogger<EnvironmentReadyCheckMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task Invoke(HttpContext httpContext, IScriptWebHostEnvironment webHostEnvironment)
        {
            _logger.LogInformation($"{nameof(EnvironmentReadyCheckMiddleware)} Invoke");
            if (webHostEnvironment.DelayRequestsEnabled)
            {
                _logger.LogInformation($"{nameof(EnvironmentReadyCheckMiddleware)} waiting for DelayRequestsEnabled");
                await webHostEnvironment.DelayCompletionTask;
                _logger.LogInformation($"{nameof(EnvironmentReadyCheckMiddleware)} done wait for DelayRequestsEnabled");
            }

            await _next.Invoke(httpContext);
        }
    }
}