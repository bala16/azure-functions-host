// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class HostNameProviderTests
    {
        private readonly Mock<IEnvironment> _mockEnvironment;
        private readonly HostNameProvider _hostNameProvider;
        private readonly TestLoggerProvider _loggerProvider;

        public HostNameProviderTests()
        {
            _mockEnvironment = new Mock<IEnvironment>(MockBehavior.Strict);

            _loggerProvider = new TestLoggerProvider();
            var loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(_loggerProvider);
            _hostNameProvider = new HostNameProvider(_mockEnvironment.Object, loggerFactory.CreateLogger<HostNameProvider>());
        }

        [Theory]
        [InlineData("test.azurewebsites.net", "test", "test.azurewebsites.net")]
        [InlineData(null, "test", "test.azurewebsites.net")]
        [InlineData("", "test", "test.azurewebsites.net")]
        [InlineData(null, null, null)]
        [InlineData("", "", "")]
        public void GetValue_ReturnsExpectedResult(string hostName, string siteName, string expected)
        {
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteHostName)).Returns(hostName);
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName)).Returns(siteName);

            Assert.Equal(expected, _hostNameProvider.Value);
        }

        [Fact]
        public void Synchronize_UpdatesValue()
        {
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteHostName)).Returns((string)null);
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName)).Returns((string)null);
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteInstanceId)).Returns("InstanceId");

            Assert.Equal(null, _hostNameProvider.Value);

            // no header present
            HttpRequest request = new DefaultHttpContext().Request;
            _hostNameProvider.Synchronize(request);
            Assert.Equal(null, _hostNameProvider.Value);

            // empty header value
            request = new DefaultHttpContext().Request;
            request.Headers.Add(ScriptConstants.AntaresDefaultHostNameHeader, string.Empty);
            _hostNameProvider.Synchronize(request);
            Assert.Equal(null, _hostNameProvider.Value);

            // host provided via header - expect update
            request = new DefaultHttpContext().Request;
            request.Headers.Add(ScriptConstants.AntaresDefaultHostNameHeader, "test.azurewebsites.net");
            _hostNameProvider.Synchronize(request);
            Assert.Equal("test.azurewebsites.net", _hostNameProvider.Value);
            var logs = _loggerProvider.GetAllLogMessages();
            Assert.Equal(1, logs.Count);
            Assert.Equal("HostName updated from '(null)' to 'test.azurewebsites.net'", logs[0].FormattedMessage);

            // no change in header value - no update expected
            _loggerProvider.ClearAllLogMessages();
            _hostNameProvider.Synchronize(request);
            Assert.Equal("test.azurewebsites.net", _hostNameProvider.Value);
            logs = _loggerProvider.GetAllLogMessages();
            Assert.Equal(0, logs.Count);

            // another change - expect update
            request = new DefaultHttpContext().Request;
            request.Headers.Add(ScriptConstants.AntaresDefaultHostNameHeader, "test2.azurewebsites.net");
            _hostNameProvider.Synchronize(request);
            Assert.Equal("test2.azurewebsites.net", _hostNameProvider.Value);
            logs = _loggerProvider.GetAllLogMessages();
            Assert.Equal(1, logs.Count);
            Assert.Equal("HostName updated from 'test.azurewebsites.net' to 'test2.azurewebsites.net'", logs[0].FormattedMessage);
        }

        [Fact]
        public void Does_Not_Update_Hostname_For_Standby_Container()
        {
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteHostName)).Returns((string)null);
            // AzureWebsiteName will be non empty for specialized containers.
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName)).Returns((string)null);
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteInstanceId)).Returns((string)null);
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.ContainerName)).Returns("CONTAINER_NAME");

            Assert.Equal(null, _hostNameProvider.Value);

            // no header present
            HttpRequest request = new DefaultHttpContext().Request;
            _hostNameProvider.Synchronize(request);
            Assert.Equal(null, _hostNameProvider.Value);

            // empty header value
            request = new DefaultHttpContext().Request;
            request.Headers.Add(ScriptConstants.AntaresDefaultHostNameHeader, string.Empty);
            _hostNameProvider.Synchronize(request);
            Assert.Equal(null, _hostNameProvider.Value);

            // host provided via header and container is not specialized yet - no update expected
            request = new DefaultHttpContext().Request;
            request.Headers.Add(ScriptConstants.AntaresDefaultHostNameHeader, "test.azurewebsites.net");
            request.Headers.Add(ScriptConstants.AntaresSiteDeploymentId, "test");
            _hostNameProvider.Synchronize(request);
            Assert.Null(_hostNameProvider.Value);
            var logs = _loggerProvider.GetAllLogMessages();
            Assert.Equal(1, logs.Count);
            Assert.Equal("Skip update HostName from '(null)' to 'test.azurewebsites.net' CurrentRuntimeSite '(null)' DeploymentId 'test'", logs[0].FormattedMessage);

            // container is specialized. Host name will be updated without synchronization
            _loggerProvider.ClearAllLogMessages();
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName)).Returns("test");
            _hostNameProvider.Synchronize(request);
            Assert.Equal("test.azurewebsites.net", _hostNameProvider.Value);
            logs = _loggerProvider.GetAllLogMessages();
            Assert.Equal(0, logs.Count);
        }

        [Fact]
        public void Does_Not_Update_Hostname_When_SiteNames_Do_Not_Match()
        {
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteHostName)).Returns((string)null);
            // Container is specialized to site "site1"
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName)).Returns("site1");
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteInstanceId)).Returns((string)null);
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.ContainerName)).Returns("CONTAINER_NAME");

            // Hostname matches specialized site name
            Assert.Equal("site1.azurewebsites.net", _hostNameProvider.Value);

            // no header present
            HttpRequest request = new DefaultHttpContext().Request;
            _hostNameProvider.Synchronize(request);
            Assert.Equal("site1.azurewebsites.net", _hostNameProvider.Value);

            // empty header value
            request = new DefaultHttpContext().Request;
            request.Headers.Add(ScriptConstants.AntaresDefaultHostNameHeader, string.Empty);
            _hostNameProvider.Synchronize(request);
            Assert.Equal("site1.azurewebsites.net", _hostNameProvider.Value);

            // host provided via header and but container specialized to different site - no update expected
            request = new DefaultHttpContext().Request;
            request.Headers.Add(ScriptConstants.AntaresDefaultHostNameHeader, "test.azurewebsites.net");
            request.Headers.Add(ScriptConstants.AntaresSiteDeploymentId, "test");
            _hostNameProvider.Synchronize(request);
            Assert.Equal("site1.azurewebsites.net", _hostNameProvider.Value);
            var logs = _loggerProvider.GetAllLogMessages();
            Assert.Equal(1, logs.Count);
            Assert.Equal("Skip update HostName from 'site1.azurewebsites.net' to 'test.azurewebsites.net' CurrentRuntimeSite 'site1' DeploymentId 'test'", logs[0].FormattedMessage);
        }

        [Fact]
        public void Updates_HostName_On_Specialization_And_Domain_Name_Change()
        {
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteHostName)).Returns((string)null);
            // AzureWebsiteName will be non empty for specialized containers.
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName)).Returns((string)null);
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteInstanceId)).Returns((string)null);
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.ContainerName)).Returns("CONTAINER_NAME");

            Assert.Equal(null, _hostNameProvider.Value);

            HttpRequest request = new DefaultHttpContext().Request;

            // host provided via header and container is not specialized yet - no update expected
            request = new DefaultHttpContext().Request;
            request.Headers.Add(ScriptConstants.AntaresDefaultHostNameHeader, "site1.azurewebsites.net");
            request.Headers.Add(ScriptConstants.AntaresSiteDeploymentId, "site1");
            _hostNameProvider.Synchronize(request);
            Assert.Null(_hostNameProvider.Value);
            var logs = _loggerProvider.GetAllLogMessages();
            Assert.Equal(1, logs.Count);
            Assert.Equal("Skip update HostName from '(null)' to 'site1.azurewebsites.net' CurrentRuntimeSite '(null)' DeploymentId 'site1'", logs[0].FormattedMessage);

            // container is specialized. Host name will be updated without synchronization
            _loggerProvider.ClearAllLogMessages();
            _mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName)).Returns("test");
            request = new DefaultHttpContext().Request;
            request.Headers.Add(ScriptConstants.AntaresDefaultHostNameHeader, "test.azurewebsites.net");
            request.Headers.Add(ScriptConstants.AntaresSiteDeploymentId, "test");
            _hostNameProvider.Synchronize(request);
            Assert.Equal("test.azurewebsites.net", _hostNameProvider.Value);
            logs = _loggerProvider.GetAllLogMessages();
            Assert.Equal(0, logs.Count);

            // host provided via header and but container specialized to different site - no update expected
            _loggerProvider.ClearAllLogMessages();
            request = new DefaultHttpContext().Request;
            request.Headers.Add(ScriptConstants.AntaresDefaultHostNameHeader, "site1.azurewebsites.net");
            request.Headers.Add(ScriptConstants.AntaresSiteDeploymentId, "site1");
            _hostNameProvider.Synchronize(request);
            Assert.Equal("test.azurewebsites.net", _hostNameProvider.Value);
            logs = _loggerProvider.GetAllLogMessages();
            Assert.Equal(1, logs.Count);
            Assert.Equal("Skip update HostName from 'test.azurewebsites.net' to 'site1.azurewebsites.net' CurrentRuntimeSite 'test' DeploymentId 'site1'", logs[0].FormattedMessage);

            // host provided via header and site names match. But hostname is being updated.
            _loggerProvider.ClearAllLogMessages();
            request = new DefaultHttpContext().Request;
            request.Headers.Add(ScriptConstants.AntaresDefaultHostNameHeader, "azurewebsites.test.com");
            request.Headers.Add(ScriptConstants.AntaresSiteDeploymentId, "test");
            _hostNameProvider.Synchronize(request);
            Assert.Equal("azurewebsites.test.com", _hostNameProvider.Value);
            logs = _loggerProvider.GetAllLogMessages();
            Assert.Equal(1, logs.Count);
            Assert.Equal("HostName updated from 'test.azurewebsites.net' to 'azurewebsites.test.com'", logs[0].FormattedMessage);
        }
    }
}
