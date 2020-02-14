// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.AppService.Middleware.AspNetCoreMiddleware;
using Microsoft.Azure.WebJobs.Script.Middleware;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Middleware
{
    public class JobHostEasyAuthMiddleware : IJobHostHttpMiddleware
    {
        private readonly ILogger<JobHostEasyAuthMiddleware> _logger;
        private RequestDelegate _invoke;

        public JobHostEasyAuthMiddleware(IOptions<HostEasyAuthOptions> hostEasyAuthOptions, ILogger<JobHostEasyAuthMiddleware> logger)
        {
            _logger = logger;
            RequestDelegate contextNext = async context =>
            {
                if (context.Items.Remove(ScriptConstants.EasyAuthMiddlewareRequestDelegate, out object requestDelegate) && requestDelegate is RequestDelegate next)
                {
                    await next(context);
                }
            };
            if (hostEasyAuthOptions.Value.SiteAuthEnabled)
            {
                _logger.LogWarning($"{nameof(JobHostEasyAuthMiddleware)} Creating {nameof(EasyAuthMiddleware)}");
                var easyAuthMiddleware = new EasyAuthMiddleware(contextNext, hostEasyAuthOptions.Value.Configuration);
                _invoke = easyAuthMiddleware.InvokeAsync;
            }
            else
            {
                _logger.LogWarning($"{nameof(JobHostEasyAuthMiddleware)} NOT Creating {nameof(EasyAuthMiddleware)}");
                _invoke = contextNext;
            }
        }

        public async Task Invoke(HttpContext context, RequestDelegate next)
        {
            context.Items.Add(ScriptConstants.EasyAuthMiddlewareRequestDelegate, next);
            await _invoke(context);
        }
    }
}