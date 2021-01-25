// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.WebHost.ContainerManagement
{
    public class SingleStreamerService
    {
        private readonly ZipFileDownloadService _zipFileDownloadService;
        private readonly ILogger<SingleStreamerService> _logger;
        private long _totalBytes = long.MinValue;

        public SingleStreamerService(ZipFileDownloadService zipFileDownloadService, ILogger<SingleStreamerService> logger)
        {
            _zipFileDownloadService = zipFileDownloadService;
            _logger = logger;
        }

        public async Task HandleMetadata(MultipartSection section)
        {
            _logger.LogInformation($"{nameof(HandleMetadata)} Start");
            _zipFileDownloadService.NotifyDownloadStart();

            try
            {
                if (section == null)
                {
                    throw new ArgumentException(nameof(section));
                }

                using (var streamReader = new StreamReader(section.Body))
                {
                    string metadataContent = await streamReader.ReadToEndAsync();
                    var zipMetadata = JsonConvert.DeserializeObject<ZipMetadata>(metadataContent);
                    _logger.LogInformation($"{nameof(HandleMetadata)} ZipMetadata read = {zipMetadata}");

                    SetTotalBytes(zipMetadata.SizeInBytes);
                }
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, nameof(HandleMetadata));
                _zipFileDownloadService.NotifyDownloadComplete(null);
            }
        }

        private void SetTotalBytes(long length)
        {
            _totalBytes = length;
            _logger.LogInformation($"Initializing total bytes to {_totalBytes}");
        }

        private static string GetZipDestinationPath()
        {
            var fileName = Environment.GetEnvironmentVariable(EnvironmentSettingNames.ContainerName) ?? "zip-stream";
            var tmpPath = Path.GetTempPath();
            return Path.Combine(tmpPath, $"{fileName}.zip");
        }

        public async Task WriteStreamToFileChunked(Stream stream)
        {
            try
            {
                var stopwatch = Stopwatch.StartNew();
                _logger.LogInformation($"{nameof(WriteStreamToFileChunked)} Writing to file stream from ms");

                var buffer = new byte[32 * 1024];
                var hasMore = true;
                long totalBytesRead = 0;

                using (var fs = new FileStream(GetZipDestinationPath(), FileMode.CreateNew,
                    FileAccess.Write, FileShare.Read | FileShare.Delete, 4 * 1024, FileOptions.Asynchronous))
                {
                    do
                    {
                        var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                        if (bytesRead == 0)
                        {
                            hasMore = false;
                            TriggerProgressChanged(_totalBytes, totalBytesRead);
                            continue;
                        }

                        await fs.WriteAsync(buffer, 0, bytesRead);

                        totalBytesRead += bytesRead;
                        TriggerProgressChanged(_totalBytes, totalBytesRead);
                    }
                    while (hasMore);

                    _logger.LogInformation("{nameof(WriteToFileUsingStream)} All bytes written");
                }

                _logger.LogInformation("{nameof(WriteToFileUsingStream)} All bytes flushed. Signalling download complete");
                _zipFileDownloadService.NotifyDownloadComplete(GetZipDestinationPath());

                stopwatch.Stop();
                _logger.LogInformation($"{nameof(WriteStreamToFileChunked)} Copy from ms to fs = {stopwatch.Elapsed.TotalMilliseconds}");
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, nameof(WriteStreamToFileChunked));
                _zipFileDownloadService.NotifyDownloadComplete(null);
            }
        }

        private void TriggerProgressChanged(long totalDownloadSize, long totalBytesRead)
        {
            double progressPercentage = Math.Round((double)totalBytesRead / totalDownloadSize * 100, 2);
            _logger.LogInformation($"[Progress] TotalSize = {totalDownloadSize} ReadSoFar = {totalBytesRead} % = {progressPercentage}");
        }

        public async Task WriteZipContentToFileInChunks(MultipartSection zipContentSection)
        {
            try
            {
                _logger.LogInformation($"{nameof(WriteZipContentToFileInChunks)} Start {nameof(WriteZipContentToFileInChunks)}");
                if (zipContentSection == null)
                {
                    throw new ArgumentException(nameof(zipContentSection));
                }

                var totalBytes = zipContentSection.Body.Length;
                await WriteStreamToFileChunked(zipContentSection.Body);
                _logger.LogInformation($"{nameof(WriteZipContentToFileInChunks)} Total bytes copied = {totalBytes}");
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, nameof(WriteZipContentToFileInChunks));
                _zipFileDownloadService.NotifyDownloadComplete(null);
            }
        }

        public async Task WriteZipContentToFileDirectly(MultipartSection zipContentSection)
        {
            try
            {
                _logger.LogInformation($"{nameof(WriteZipContentToFileDirectly)} {nameof(MultipartSection)}");
                if (zipContentSection == null)
                {
                    throw new ArgumentException(nameof(zipContentSection));
                }

                await WriteStreamToFileDirectly(zipContentSection.Body);
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, nameof(WriteZipContentToFileDirectly));
                _zipFileDownloadService.NotifyDownloadComplete(null);
            }
        }

        public async Task WriteStreamToFileDirectly(Stream stream)
        {
            _logger.LogInformation($"{nameof(WriteStreamToFileDirectly)} {nameof(Stream)}");

            using (var fs = new FileStream(GetZipDestinationPath(), FileMode.CreateNew,
                FileAccess.Write))
            {
                await stream.CopyToAsync(fs);
                _logger.LogInformation($"{nameof(WriteStreamToFileDirectly)} All bytes copied to file stream. fs.Length = {fs.Length}");
            }

            _logger.LogInformation($"{nameof(WriteStreamToFileDirectly)} All bytes flushed. Signalling download complete");
            _zipFileDownloadService.NotifyDownloadComplete(GetZipDestinationPath());
        }
    }
}
