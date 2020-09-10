// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.IO.Compression;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.ContainerManagement
{
    public class FunctionsFolderNotificationService
    {
        private readonly ILogger<FunctionsFolderNotificationService> _logger;
        private readonly ManualResetEvent _evt;
        private DateTime? _startTime = null;
        private DateTime? _finishTime = null;
        private string _path = string.Empty;

        public FunctionsFolderNotificationService(ILogger<FunctionsFolderNotificationService> logger)
        {
            _logger = logger;
            _evt = new ManualResetEvent(false);
        }

        public void NotifyDownloadStart()
        {
            if (_startTime == null)
            {
                _logger.LogInformation($"Started {nameof(NotifyDownloadStart)}");
                _startTime = DateTime.UtcNow;
            }
        }

        public void NotifyDownloadComplete(string path)
        {
            _logger.LogInformation($"Download complete at {path}");
            _path = path;
            _finishTime = DateTime.UtcNow;
            _evt.Set();
        }

        private string WaitForDownload(TimeSpan timeSpan)
        {
            _logger.LogInformation($"Start {nameof(WaitForDownload)}");
            _evt.WaitOne(timeSpan);
            return _path;
        }

        private void UnzipFunctionsFolder(string filePath, string downloadPath)
        {
            var stopwatch = Stopwatch.StartNew();
            _logger.LogInformation($"Extracting functions folder files to '{downloadPath}'");
            ZipFile.ExtractToDirectory(filePath, downloadPath, overwriteFiles: true);
            stopwatch.Stop();
            _logger.LogInformation($"Zip extraction complete for functions folders in {stopwatch.Elapsed.TotalMilliseconds}");
        }

        public string WaitForUnZip(TimeSpan timeSpan)
        {
            try
            {
                var path = WaitForDownload(timeSpan);
                if (string.IsNullOrEmpty(path))
                {
                    _logger.LogWarning($"Failed to fetch FunctionsFolder");
                    return string.Empty;
                }
                else
                {
                    // Unzip to /opt folder
                    UnzipFunctionsFolder(path, "/opt");
                    return path;
                }
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, nameof(WaitForUnZip));
                return string.Empty;
            }
        }
    }
}
