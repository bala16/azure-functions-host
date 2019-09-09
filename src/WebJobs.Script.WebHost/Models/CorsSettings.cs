// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Models
{
    public class CorsSettings
    {
        public IEnumerable<string> AllowedOrigins { get; set; }

        public bool SupportCredentials { get; set; }
    }
}