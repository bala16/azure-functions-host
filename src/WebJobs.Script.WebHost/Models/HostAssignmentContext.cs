// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Models
{
    public class HostAssignmentContext
    {
        [JsonProperty("siteId")]
        public int SiteId { get; set; }

        [JsonProperty("siteName")]
        public string SiteName { get; set; }

        [JsonProperty("environment")]
        public Dictionary<string, string> Environment { get; set; }

        [JsonProperty("lastModifiedTime")]
        public DateTime LastModifiedTime { get; set; }

        public string ZipUrl
        {
            get
            {
                if (Environment.ContainsKey(EnvironmentSettingNames.AzureWebsiteAltZipDeployment))
                {
                    return Environment[EnvironmentSettingNames.AzureWebsiteAltZipDeployment];
                }
                else if (Environment.ContainsKey(EnvironmentSettingNames.AzureWebsiteZipDeployment))
                {
                    return Environment[EnvironmentSettingNames.AzureWebsiteZipDeployment];
                }
                else
                {
                    return string.Empty;
                }
            }
        }

        public bool Equals(HostAssignmentContext other)
        {
            if (other == null)
            {
                return false;
            }

            return SiteId == other.SiteId && LastModifiedTime.CompareTo(other.LastModifiedTime) == 0;
        }

        public void ApplyAppSettings(ILogger logger)
        {
            var found = false;
            foreach (var pair in Environment)
            {
                if (pair.Key.Equals("AzureWebEncryptionKey", StringComparison.OrdinalIgnoreCase))
                {
                    found = true;
                    logger.LogInformation("AzureWebEncryptionKey {0} : {1}", pair.Key, pair.Value);
                }
                System.Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            }

            if (!found)
            {
                logger.LogInformation("AzureWebEncryptionKey NOT FOUND");
            }
        }
    }
}
