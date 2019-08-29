// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Middleware
{
    public class HostnameFixupMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly HostNameProvider _hostNameProvider;

        public HostnameFixupMiddleware(RequestDelegate next, HostNameProvider hostNameProvider)
        {
            _next = next;
            _hostNameProvider = hostNameProvider;
        }

        public async Task Invoke(HttpContext context)
        {
            Console.WriteLine("In HostnameFixupMiddleware " + context.Request.GetDisplayUrl());

            _hostNameProvider.Synchronize(context.Request);

            await _next.Invoke(context);
        }
    }
}
