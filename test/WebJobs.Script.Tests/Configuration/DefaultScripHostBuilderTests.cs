// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Host;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Configuration
{
    public class DefaultScripHostBuilderTests
    {
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ReturnsScriptHostStandbyStatus(bool isStandbyMode)
        {
            var serviceCollection = new ServiceCollection();
            serviceCollection.AddSingleton<ILoggerFactory>(new LoggerFactory());
            var dependencyValidator = new Mock<IDependencyValidator>();
            serviceCollection.AddSingleton(dependencyValidator.Object);
            serviceCollection.AddSingleton<IMetricsLogger, TestMetricsLogger>();

            var testEnvironment = new TestEnvironment(new Dictionary<string, string>()
                { [EnvironmentSettingNames.AzureMonitorCategories] = string.Empty });
            serviceCollection.AddSingleton<IEnvironment>(testEnvironment);
            var debugStateProvider = new Mock<IDebugStateProvider>();
            serviceCollection.AddSingleton(debugStateProvider.Object);
            var functionMetadataManager = new Mock<IFunctionMetadataManager>();
            serviceCollection.AddSingleton(functionMetadataManager.Object);

            var rootServiceProvider = serviceCollection.BuildServiceProvider();

            var defaultScriptHostBuilder = new DefaultScriptHostBuilder(
                new TestOptionsMonitor<ScriptApplicationHostOptions>(() =>
                {
                    var scriptApplicationHostOptions = new ScriptApplicationHostOptions
                    {
                        ScriptPath = string.Empty,
                        LogPath = string.Empty,
                        RootServiceProvider = rootServiceProvider
                    };
                    return scriptApplicationHostOptions;
                }),
                rootServiceProvider, new Mock<IServiceScopeFactory>().Object,
                new TestOptionsMonitor<StandbyOptions>(new StandbyOptions() { InStandbyMode = isStandbyMode }));

            var host = defaultScriptHostBuilder.BuildHost(true, true);
            var scriptHostStandbyStateProvider = host.Services.GetService<ScriptHostStandbyStateProvider>();
            Assert.Equal(isStandbyMode, scriptHostStandbyStateProvider.IsStandbyScriptHost);
        }
    }
}
