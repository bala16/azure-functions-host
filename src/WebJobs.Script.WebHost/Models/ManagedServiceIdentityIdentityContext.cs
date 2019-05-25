// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.WebHost.Models
{
    public class ManagedServiceIdentityIdentityContext
    {
        public string ContainerName { get; set; }

        public string StampName { get; set; }

        public string TenantId { get; set; }

        public string SiteName { get; set; }
    }
}
