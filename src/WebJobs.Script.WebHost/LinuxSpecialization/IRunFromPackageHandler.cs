// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;

namespace Microsoft.Azure.WebJobs.Script.WebHost.LinuxSpecialization
{
    public interface IRunFromPackageHandler
    {
        Task<bool> DeployToLocalDisk(HostAssignmentContext assignmentContext, RunFromPackageContext pkgContext);

        Task<bool> DeployToAzureFiles(HostAssignmentContext assignmentContext, RunFromPackageContext pkgContext);

        Task<string> Download(RunFromPackageContext pkgContext);
    }
}
