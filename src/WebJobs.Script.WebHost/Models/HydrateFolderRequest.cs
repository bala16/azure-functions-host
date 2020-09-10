// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.WebHost.Models
{
    public class HydrateFolderRequest
    {
        public string SiteName { get; set; }

        public string IpAddress { get; set; }

        public string FunctionFolderUri { get; set; }

        public string FunctionFolderUriVersion { get; set; }
    }
}
