// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public class EncyptedHostAssignmentContextReader : IEncyptedHostAssignmentContextReader
    {
        public async Task<string> Read(string uri, CancellationToken cancellationToken)
        {
            var cloudBlockBlob = new CloudBlockBlob(new Uri(uri));
            if (await cloudBlockBlob.ExistsAsync(null, null, cancellationToken))
            {
                return await cloudBlockBlob.DownloadTextAsync(null, null, null, null, cancellationToken);
            }

            return string.Empty;
        }
    }
}
