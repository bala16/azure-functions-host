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
        private DateTime? _startTime = null;
        private DateTime? _finishTime = null;

        public ZipFileDownloadService(ILogger<ZipFileDownloadService> logger)
        {
            _logger = logger;
            _evt = new ManualResetEvent(false);
            _logger.LogInformation($"BBB Starting {nameof(ZipFileDownloadService)}");
        }

        public void NotifyDownloadStart()
        {
            if (_startTime == null)
            {
                _logger.LogInformation($"BBB XStream download started");
                _startTime = DateTime.UtcNow;
            }
        }

        private void MarkComplete()
        {
            if (_finishTime == null)
            {
                _logger.LogInformation("BBB XStream download finished");
                _finishTime = DateTime.UtcNow;
            }
            else
            {
                throw new Exception($"Multiple completes");
            }
        }

        public void LogTimeTaken()
        {
            TimeSpan timeSpan = _finishTime.Value.Subtract(_startTime.Value);
            _logger.LogInformation($"BBB Total XStream time taken ms = {timeSpan.TotalMilliseconds}");
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
            MarkComplete();
            _evt.Set();
        }
    }
}
