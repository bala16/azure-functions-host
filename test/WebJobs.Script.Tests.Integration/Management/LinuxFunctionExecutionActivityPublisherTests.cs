﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.ContainerManagement;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.Management
{
    public class LinuxFunctionExecutionActivityPublisherTests
    {
        private const int FlushIntervalMs = 2;
        private const int DelayIntervalMs = 500;

        [Fact]
        public async Task PublishesFunctionExecutionActivity()
        {
            var activity = new ContainerFunctionExecutionActivity(DateTime.MinValue, "func-1",
                ExecutionStage.InProgress, "trigger-1", false);

            var environment = new TestEnvironment();
            environment.SetEnvironmentVariable(EnvironmentSettingNames.ContainerName, "Container-Name");
            var scriptWebEnvironment = new ScriptWebHostEnvironment(environment);

            var meshClient = new Mock<IMeshInitServiceClient>();
            meshClient.Setup(c =>
                c.PublishContainerFunctionExecutionActivities(
                    It.IsAny<IEnumerable<ContainerFunctionExecutionActivity>>())).Returns(Task.FromResult(true));

            using (var publisher = new LinuxFunctionExecutionActivityPublisher(meshClient.Object, scriptWebEnvironment,
                environment, NullLogger<LinuxFunctionExecutionActivityPublisher>.Instance, FlushIntervalMs))
            {
                await publisher.StartAsync(CancellationToken.None);
                publisher.PublishFunctionExecutionActivity(activity);
                await Task.Delay(DelayIntervalMs);
                await publisher.StopAsync(CancellationToken.None);

                meshClient.Verify(
                    c => c.PublishContainerFunctionExecutionActivities(
                        It.Is<IEnumerable<ContainerFunctionExecutionActivity>>(e =>
                            MatchesFunctionActivities(e, activity))), Times.Once);

            }
        }

        [Fact]
        public async Task DoesNotPublishExecutionActivityInPlaceholderMode()
        {
            var activity = new ContainerFunctionExecutionActivity(DateTime.MinValue, "func-1",
                ExecutionStage.InProgress, "trigger-1", false);

            var environment = new TestEnvironment();
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");
            environment.SetEnvironmentVariable(EnvironmentSettingNames.ContainerName, "Container-Name");
            var scriptWebEnvironment = new ScriptWebHostEnvironment(environment);

            var meshClient = new Mock<IMeshInitServiceClient>();

            using (var publisher = new LinuxFunctionExecutionActivityPublisher(meshClient.Object, scriptWebEnvironment,
                environment, NullLogger<LinuxFunctionExecutionActivityPublisher>.Instance, FlushIntervalMs))
            {
                await publisher.StartAsync(CancellationToken.None);
                publisher.PublishFunctionExecutionActivity(activity);
                await Task.Delay(DelayIntervalMs);
                await publisher.StopAsync(CancellationToken.None);
                meshClient.Verify(c =>
                    c.PublishContainerFunctionExecutionActivities(It.IsAny<IEnumerable<ContainerFunctionExecutionActivity>>()), Times.Never);
            }
        }

        [Fact]
        public async Task DoesNotPublishFunctionExecutionActivityForNonLinuxConsumptionApps()
        {
            var activity = new ContainerFunctionExecutionActivity(DateTime.MinValue, "func-1",
                ExecutionStage.InProgress, "trigger-1", false);

            var environment = new TestEnvironment();
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");
            environment.SetEnvironmentVariable(EnvironmentSettingNames.ContainerName, string.Empty);
            var scriptWebEnvironment = new ScriptWebHostEnvironment(environment);

            var meshClient = new Mock<IMeshInitServiceClient>();

            using (var publisher = new LinuxFunctionExecutionActivityPublisher(meshClient.Object, scriptWebEnvironment,
                environment, NullLogger<LinuxFunctionExecutionActivityPublisher>.Instance, FlushIntervalMs))
            {
                await publisher.StartAsync(CancellationToken.None);
                publisher.PublishFunctionExecutionActivity(activity);
                await Task.Delay(DelayIntervalMs);
                await publisher.StopAsync(CancellationToken.None);
                meshClient.Verify(c =>
                    c.PublishContainerFunctionExecutionActivities(It.IsAny<IEnumerable<ContainerFunctionExecutionActivity>>()), Times.Never);
            }
        }

        [Fact]
        public async Task PublishesUniqueFunctionExecutionActivitiesOnly()
        {
            // activity1 and activity2 are duplicates. so only activity2 will be published
            var activity1 = new ContainerFunctionExecutionActivity(DateTime.MinValue, "func-1",
                ExecutionStage.InProgress, "trigger-1", false);
            var activity2 = new ContainerFunctionExecutionActivity(DateTime.MaxValue, "func-1",
                ExecutionStage.InProgress, "trigger-1", false);
            var activity3 = new ContainerFunctionExecutionActivity(DateTime.MaxValue, "func-1", ExecutionStage.Finished,
                "trigger-1", true);
            var activity4 = new ContainerFunctionExecutionActivity(DateTime.MaxValue, "func-1", ExecutionStage.Finished,
                "trigger-1", false);

            var environment = new TestEnvironment();
            environment.SetEnvironmentVariable(EnvironmentSettingNames.ContainerName, "Container-Name");
            var scriptWebEnvironment = new ScriptWebHostEnvironment(environment);

            var meshClient = new Mock<IMeshInitServiceClient>();

            using (var publisher = new LinuxFunctionExecutionActivityPublisher(meshClient.Object, scriptWebEnvironment,
                environment, NullLogger<LinuxFunctionExecutionActivityPublisher>.Instance, FlushIntervalMs))
            {
                await publisher.StartAsync(CancellationToken.None);
                publisher.PublishFunctionExecutionActivity(activity1);
                publisher.PublishFunctionExecutionActivity(activity2);
                publisher.PublishFunctionExecutionActivity(activity3);
                publisher.PublishFunctionExecutionActivity(activity4);
                await Task.Delay(DelayIntervalMs);
                await publisher.StopAsync(CancellationToken.None);
                meshClient.Verify(
                    c => c.PublishContainerFunctionExecutionActivities(
                        It.Is<IEnumerable<ContainerFunctionExecutionActivity>>(e =>
                            MatchesFunctionActivities(e, activity2, activity3, activity4))), Times.Once);

            }
        }

        private static bool MatchesFunctionActivities(IEnumerable<ContainerFunctionExecutionActivity> activities,
            params ContainerFunctionExecutionActivity[] expectedActivities)
        {
            if (activities.Count() != expectedActivities.Length)
            {
                return false;
            }

            return expectedActivities.All(activities.Contains);
        }
    }
}
