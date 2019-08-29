// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Azure.WebJobs.Script.WebHost.Middleware;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Middleware
{
    public class WorkerValidationMiddlewareTests
    {
        private readonly TestLoggerProvider _loggerProvider;
        private readonly LoggerFactory _loggerFactory = new LoggerFactory();

        public WorkerValidationMiddlewareTests()
        {
            _loggerProvider = new TestLoggerProvider();
            _loggerFactory.AddProvider(_loggerProvider);
        }

        [Fact]
        public async Task Returns_Invalid_cache_Response_For_Wrong_Worker()
        {
            RequestDelegate requestDelegate = async (HttpContext context) =>
            {
                await Task.Delay(0);
            };

            var mockEnvironment = new Mock<IEnvironment>(MockBehavior.Strict);

            mockEnvironment.Setup(env => env.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName))
                .Returns("site1");

            var workerValidationMiddleware = new WorkerValidationMiddleware(requestDelegate, mockEnvironment.Object,
                _loggerFactory.CreateLogger<WorkerValidationMiddleware>());

            var httpContext = CreateHttpContext();
            var requestFeature = httpContext.Request.HttpContext.Features.Get<IHttpRequestFeature>();
            requestFeature.Headers.Add(ScriptConstants.AntaresSiteDeploymentId, "site2");

            await workerValidationMiddleware.Invoke(httpContext);

            var allLogMessages = _loggerProvider.GetAllLogMessages();
            Assert.Equal(2, allLogMessages.Count);
        }

        [Fact]
        public async Task Invokes_Next_Middleware()
        {
            RequestDelegate requestDelegate = async (HttpContext context) =>
            {
                await Task.Delay(0);
                throw new Exception("Middleware exception");
            };

            var mockEnvironment = new Mock<IEnvironment>(MockBehavior.Strict);

            mockEnvironment.Setup(env => env.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName))
                .Returns("site1");

            var workerValidationMiddleware = new WorkerValidationMiddleware(requestDelegate, mockEnvironment.Object,
                _loggerFactory.CreateLogger<WorkerValidationMiddleware>());

            var httpContext = CreateHttpContext();
            var requestFeature = httpContext.Request.HttpContext.Features.Get<IHttpRequestFeature>();
            requestFeature.Headers.Add(ScriptConstants.AntaresSiteDeploymentId, "site1");

            await workerValidationMiddleware.Invoke(httpContext);

            var allLogMessages = _loggerProvider.GetAllLogMessages();
            Assert.Equal(1, allLogMessages.Count);
        }

        private HttpContext CreateHttpContext()
        {
            string requestId = Guid.NewGuid().ToString();
            var context = new DefaultHttpContext();
            Uri uri = new Uri("http://functions.com");
            var requestFeature = context.Request.HttpContext.Features.Get<IHttpRequestFeature>();
            requestFeature.Method = "GET";
            requestFeature.Scheme = uri.Scheme;
            requestFeature.Path = uri.GetComponents(UriComponents.KeepDelimiter | UriComponents.Path, UriFormat.Unescaped);
            requestFeature.PathBase = string.Empty;
            requestFeature.QueryString = uri.GetComponents(UriComponents.KeepDelimiter | UriComponents.Query, UriFormat.Unescaped);

            var headers = new HeaderDictionary();
            headers.Add(ScriptConstants.AntaresLogIdHeaderName, new StringValues(requestId));
            headers.Add("Host", new StringValues("hostheader"));
            requestFeature.Headers = headers;

            return context;
        }
    }
}
