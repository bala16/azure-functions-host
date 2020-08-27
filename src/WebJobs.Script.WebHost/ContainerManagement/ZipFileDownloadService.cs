// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.ContainerManagement
{
    public class ZipFileDownloadService
    {
        private readonly ILogger<ZipFileDownloadService> _logger;
        private readonly ManualResetEvent _evt;
        private string _path = string.Empty;

        public ZipFileDownloadService(ILogger<ZipFileDownloadService> logger)
        {
            _logger = logger;
            _evt = new ManualResetEvent(false);
            _logger.LogInformation($"BBB Starting {nameof(ZipFileDownloadService)}");
        }

        public string WaitForDownload(TimeSpan timeSpan)
        {
            _logger.LogInformation("BBB Waiting for download complete");
            _evt.WaitOne(timeSpan);
            return _path;
        }

        public void NotifyDownloadComplete(string path)
        {
            _path = path;
            _logger.LogInformation($"BBB Marking download complete = {_path}");
            _evt.Set();
        }
    }
}
