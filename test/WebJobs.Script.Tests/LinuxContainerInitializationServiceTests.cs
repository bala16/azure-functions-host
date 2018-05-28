using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Azure.WebJobs.Script.WebHost.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Newtonsoft.Json;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class LinuxContainerInitializationServiceTests : IDisposable
    {
        private const string HttpsContainerstartcontexturi = "https://containerstartcontexturi";
        private readonly Mock<IInstanceManager> _instanceManagerMock;
        private readonly Mock<IEncyptedHostAssignmentContextReader> _hostAssignmentContextReader;
        private readonly LinuxContainerInitializationService _linuxContainerInitializationService;

        public LinuxContainerInitializationServiceTests()
        {
            _instanceManagerMock = new Mock<IInstanceManager>(MockBehavior.Strict);
            _hostAssignmentContextReader = new Mock<IEncyptedHostAssignmentContextReader>(MockBehavior.Strict);
            _linuxContainerInitializationService = new LinuxContainerInitializationService(new ScriptSettingsManager(), _instanceManagerMock.Object, _hostAssignmentContextReader.Object, NullLoggerFactory.Instance);
        }

        [Fact]
        public void Runs_In_Linux_Container_Mode_Only()
        {
            var settingsManagerMock = new Mock<ScriptSettingsManager>(MockBehavior.Strict, null);
            var linuxContainerInitializationService = new LinuxContainerInitializationService(settingsManagerMock.Object, _instanceManagerMock.Object, null, NullLoggerFactory.Instance);
            settingsManagerMock.Setup(manager => manager.IsLinuxContainerEnvironment).Returns(false);
            linuxContainerInitializationService.Run(CancellationToken.None).Wait();
            settingsManagerMock.Verify(settingsManager => settingsManager.GetSetting(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public void Assigns_Context_From_CONTAINER_START_CONTEXT()
        {
            var containerEncryptionKey = TestHelpers.GenerateKeyHexString();
            var hostAssignmentContext = GetHostAssignmentContext();
            var encryptedHostAssignmentContext = GetEncryptedHostAssignmentContext(hostAssignmentContext, containerEncryptionKey);
            var serializedContext = JsonConvert.SerializeObject(new { encryptedContext = encryptedHostAssignmentContext});

            var vars = new Dictionary<string, string>
            {
                { EnvironmentSettingNames.ContainerStartContext, serializedContext },
                { EnvironmentSettingNames.ContainerEncryptionKey, containerEncryptionKey },
            };

            // Enable Linux Container
            AddLinuxContainerSettings(true, vars);

            _instanceManagerMock.Setup(manager => manager.StartAssignment(It.Is<HostAssignmentContext>(context => hostAssignmentContext.Equals(context)))).Returns(true);

            using (var env = new TestScopedEnvironmentVariable(vars))
            {
                _linuxContainerInitializationService.Run(CancellationToken.None).Wait();
            }

            _instanceManagerMock.Verify(manager => manager.StartAssignment(It.Is<HostAssignmentContext>(context => hostAssignmentContext.Equals(context))), Times.Once);
        }

        [Fact]
        public void Assigns_Context_From_CONTAINER_START_CONTEXT_SAS_URI_If_CONTAINER_START_CONTEXT_Absent()
        {
            var containerEncryptionKey = TestHelpers.GenerateKeyHexString();
            var hostAssignmentContext = GetHostAssignmentContext();
            var encryptedHostAssignmentContext = GetEncryptedHostAssignmentContext(hostAssignmentContext, containerEncryptionKey);
            var serializedContext = JsonConvert.SerializeObject(new { encryptedContext = encryptedHostAssignmentContext });

            var vars = new Dictionary<string, string>
            {
                { EnvironmentSettingNames.ContainerStartContextSasUri, HttpsContainerstartcontexturi },
                { EnvironmentSettingNames.ContainerEncryptionKey, containerEncryptionKey },
            };

            AddLinuxContainerSettings(true, vars);

            _hostAssignmentContextReader.Setup(reader => reader.Read(HttpsContainerstartcontexturi, It.IsAny<CancellationToken>())).Returns(Task.FromResult(serializedContext));
            _instanceManagerMock.Setup(manager => manager.StartAssignment(It.Is<HostAssignmentContext>(context => hostAssignmentContext.Equals(context)))).Returns(true);

            using (var env = new TestScopedEnvironmentVariable(vars))
            {
                _linuxContainerInitializationService.Run(CancellationToken.None).Wait();
            }

            _hostAssignmentContextReader.Verify(reader =>
                reader.Read(HttpsContainerstartcontexturi, It.IsAny<CancellationToken>()), Times.Once);
            _instanceManagerMock.Verify(manager => manager.StartAssignment(It.Is<HostAssignmentContext>(context => hostAssignmentContext.Equals(context))), Times.Once);
        }

        [Fact]
        public void Does_Not_Assign_If_Context_Not_Available()
        {
            var vars = new Dictionary<string, string>();
            AddLinuxContainerSettings(true, vars);

            using (var env = new TestScopedEnvironmentVariable(vars))
            {
                _linuxContainerInitializationService.Run(CancellationToken.None).Wait();
            }

            _instanceManagerMock.Verify(manager => manager.StartAssignment(It.IsAny<HostAssignmentContext>()), Times.Never);
        }

        private static string GetEncryptedHostAssignmentContext(HostAssignmentContext hostAssignmentContext, string containerEncryptionKey)
        {
            using (var env = new TestScopedEnvironmentVariable(EnvironmentSettingNames.WebSiteAuthEncryptionKey, containerEncryptionKey))
            {
                var serializeObject = JsonConvert.SerializeObject(hostAssignmentContext);
                return SimpleWebTokenHelper.Encrypt(serializeObject);
            }
        }

        private static HostAssignmentContext GetHostAssignmentContext()
        {
            var hostAssignmentContext = new HostAssignmentContext();
            hostAssignmentContext.SiteId = 1;
            hostAssignmentContext.SiteName = "sitename";
            hostAssignmentContext.LastModifiedTime = DateTime.UtcNow.Add(TimeSpan.FromMinutes(new Random().Next()));
            hostAssignmentContext.Environment = new Dictionary<string, string>();
            hostAssignmentContext.Environment.Add(EnvironmentSettingNames.AzureWebsiteAltZipDeployment, "https://zipurl.zip");
            return hostAssignmentContext;
        }

        private static void AddLinuxContainerSettings(bool isLinuxContainer, IDictionary<string, string> existing)
        {
            if (isLinuxContainer)
            {
                existing[EnvironmentSettingNames.AzureWebsiteInstanceId] = string.Empty;
                existing[EnvironmentSettingNames.ContainerName] = "ContainerName";
            }
        }

        public void Dispose()
        {
            _instanceManagerMock.Reset();
            _hostAssignmentContextReader.Reset();
        }
    }
}
