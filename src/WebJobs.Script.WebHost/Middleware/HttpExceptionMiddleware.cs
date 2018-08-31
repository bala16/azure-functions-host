// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Middleware
{
    internal class HttpExceptionMiddleware
    {
        private readonly RequestDelegate next;
        private readonly ILogger logger;

        public HttpExceptionMiddleware(RequestDelegate next, ILoggerFactory loggerFactory)
        {
            this.next = next;
            this.logger = loggerFactory.CreateLogger(ScriptConstants.LogCategoryHostGeneral);
        }

        public async Task Invoke(HttpContext context)
        {
            try
            {
                logger.LogInformation("Invoke from HttpExceptionMiddleware");
                await this.next.Invoke(context);
            }
            catch (HttpException httpException)
            {
                context.Response.StatusCode = httpException.StatusCode;
                var responseFeature = context.Features.Get<IHttpResponseFeature>();
                responseFeature.ReasonPhrase = httpException.Message;
                logger.LogInformation("httpException");
                logger.LogInformation(httpException.StatusCode + " " + httpException.Message);
            }
        }
    }
}
