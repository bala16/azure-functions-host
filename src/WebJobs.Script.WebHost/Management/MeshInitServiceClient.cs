// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Management
{
    public class MeshInitServiceClient : IMeshInitServiceClient
    {
        private readonly HttpClient _client;
        private readonly ILogger _logger;
        private readonly IEnvironment _environment;

        public MeshInitServiceClient(HttpClient client, IEnvironment environment, ILogger<MeshInitServiceClient> logger)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        }

        public async Task MountCifs(string connectionString, string contentShare, string targetPath)
        {
            var sa = CloudStorageAccount.Parse(connectionString);
            var key = Convert.ToBase64String(sa.Credentials.ExportKey());
            await SendAsync(new[]
            {
                new KeyValuePair<string, string>("operation", "cifs"),
                new KeyValuePair<string, string>("host", sa.FileEndpoint.Host),
                new KeyValuePair<string, string>("accountName", sa.Credentials.AccountName),
                new KeyValuePair<string, string>("accountKey", key),
                new KeyValuePair<string, string>("contentShare", contentShare),
                new KeyValuePair<string, string>("targetPath", targetPath),
            });
        }

        public Task MountBlob(string connectionString, string contentShare, string targetPath)
        {
            // todo: Implement once mesh init server supports mounting blobs
            throw new NotImplementedException(nameof(MountBlob));
        }

        public async Task MountFuse(string type, string filePath, string scriptPath)
            => await SendAsync(new[]
            {
                new KeyValuePair<string, string>("operation", type),
                new KeyValuePair<string, string>("filePath", filePath),
                new KeyValuePair<string, string>("targetPath", scriptPath),
            });

        public async Task PublishContainerFunctionExecutionActivities(IEnumerable<ContainerFunctionExecutionActivity> activities)
        {
            var meshInitServiceUri = _environment.GetEnvironmentVariable(EnvironmentSettingNames.MeshInitURI);
            _logger.LogInformation($"Publishing {activities.Count()} function execution activities to {meshInitServiceUri}");

            foreach (var activity in activities)
            {
                try
                {
                    await Utility.InvokeWithRetriesAsync(async () =>
                    {
                        await PublishContainerFunctionExecutionActivity(activity);
                    }, 2, TimeSpan.FromSeconds(0.5));
                }
                catch (Exception e)
                {
                    _logger.LogError(e, $"{nameof(PublishContainerFunctionExecutionActivities)}");
                }
            }
        }

        private async Task PublishContainerFunctionExecutionActivity(ContainerFunctionExecutionActivity activity)
        {
            var operation = new[]
            {
                new KeyValuePair<string, string>("operation", "add-fes"),
                new KeyValuePair<string, string>("content", JsonConvert.SerializeObject(activity)),
            };
            _logger.LogInformation($"Publishing function execution activity {activity}");

            var response = await SendAsync(operation);
            response.EnsureSuccessStatusCode();
            _logger.LogInformation($"Successfully published function execution activity {activity}");
        }

        private async Task<HttpResponseMessage> SendAsync(IEnumerable<KeyValuePair<string, string>> formData)
        {
            var res = await _client.PostAsync(_environment.GetEnvironmentVariable(EnvironmentSettingNames.MeshInitURI),
                new FormUrlEncodedContent(formData));
            _logger.LogInformation("Response {res} from init", res);
            return res;
        }
    }
}