// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Management
{
    public class NullMeshServiceClient : IMeshServiceClient
    {
        private static readonly Lazy<NullMeshServiceClient> _instance = new Lazy<NullMeshServiceClient>(new NullMeshServiceClient());

        private NullMeshServiceClient()
        {
        }

        public static NullMeshServiceClient Instance => _instance.Value;

        public Task<HttpResponseMessage> MountCifs(string connectionString, string contentShare, string targetPath)
        {
            return Task.FromResult(new HttpResponseMessage());
        }

        public Task MountBlob(string connectionString, string contentShare, string targetPath)
        {
            return Task.CompletedTask;
        }

        public Task<HttpResponseMessage> MountFuse(string type, string filePath, string scriptPath)
        {
            return Task.FromResult(new HttpResponseMessage());
        }

        public Task PublishContainerFunctionExecutionActivity(ContainerFunctionExecutionActivity activity)
        {
            return Task.CompletedTask;
        }

        public Task PublishContainerActivity(IEnumerable<ContainerFunctionExecutionActivity> activities)
        {
            return Task.CompletedTask;
        }
    }
}
