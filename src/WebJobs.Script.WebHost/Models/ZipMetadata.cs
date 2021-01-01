// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.WebHost.Models
{
    public class ZipMetadata // FileMetadata
    {
        public long ModifiedTimeTicks { get; set; }

        public long SizeInBytes { get; set; }

        public string ETag { get; set; }

        public int SiteId { get; set; }

        public override string ToString()
        {
            return
                $"ModifiedTimeTicks = {ModifiedTimeTicks} SizeInBytes = {SizeInBytes} ETag = {ETag} SiteId = {SiteId}";
        }
    }
}
