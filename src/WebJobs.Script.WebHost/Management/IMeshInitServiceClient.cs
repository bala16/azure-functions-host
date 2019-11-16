// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Management
{
    public interface IMeshInitServiceClient
    {
        Task MountCifs(string connectionString, string contentShare, string targetPath);

        Task MountBlob(string connectionString, string contentShare, string targetPath);

        Task MountFuse(string type, string filePath, string scriptPath);

        Task<bool> PublishContainerFunctionExecutionActivity(ContainerFunctionExecutionActivity activity);
    }
}
