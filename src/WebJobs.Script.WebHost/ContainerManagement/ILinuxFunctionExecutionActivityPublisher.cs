// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Azure.WebJobs.Script.WebHost.ContainerManagement
{
    public interface ILinuxFunctionExecutionActivityPublisher
    {
        void PublishFunctionExecutionActivity(ContainerFunctionExecutionActivity activity);
    }
}