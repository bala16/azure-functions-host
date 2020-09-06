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
        private readonly IEnvironment _environment;
        private long _totalBytes = long.MinValue;

        public SingleStreamerService(ZipFileDownloadService zipFileDownloadService, ILogger<SingleStreamerService> logger, IEnvironment environment)
        {
            _zipFileDownloadService = zipFileDownloadService;
            _logger = logger;
            _environment = environment;
        }

        public async Task HandleMetadata(MultipartSection section)
        {
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
                    _logger.LogInformation($"BBB ZipMetadata read = {zipMetadata}");

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

        public async Task HandleZipAllContent(MultipartSection zipContentSection)
        {
            try
            {
                _logger.LogInformation($"{nameof(HandleZipAllContent)}");
                if (zipContentSection == null)
                {
                    throw new ArgumentException(nameof(zipContentSection));
                }

                using (var fs = new FileStream(GetZipDestinationPath(), FileMode.CreateNew,
                    FileAccess.Write))
                {
                    await zipContentSection.Body.CopyToAsync(fs);

                    var length = zipContentSection.Body.Length;
                    _logger.LogInformation($"BBB {DateTime.UtcNow} Writing to file stream chunk. bytes = {length} fs.Length = {fs.Length}");
                    fs.Flush(); // flush once at the end?

                    if (AllBytesRead(length))
                    {
                        _logger.LogInformation("All bytes read. Signalling download complete");
                        _zipFileDownloadService.NotifyDownloadComplete(GetZipDestinationPath());
                    }
                }
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, nameof(HandleZipAllContent));
                _zipFileDownloadService.NotifyDownloadComplete(null);
            }
        }

        private async Task WriteToFileUsingMemoryStream(MemoryStream memoryStream)
        {
            try
            {
                var stopwatch = Stopwatch.StartNew();
                memoryStream.Seek(0, SeekOrigin.Begin);
                using (memoryStream)
                {
                    using (var fs = new FileStream(GetZipDestinationPath(), FileMode.CreateNew,
                        FileAccess.Write))
                    {
                        _logger.LogInformation($"BBB {DateTime.UtcNow} Writing to file stream from ms = {memoryStream.Length} fs.Length = {fs.Length}");
                        await memoryStream.CopyToAsync(fs);
                        fs.Flush(); // flush once at the end?

                        if (AllBytesRead(fs.Length))
                        {
                            _logger.LogInformation("All bytes written. Signalling download complete");
                            _zipFileDownloadService.NotifyDownloadComplete(GetZipDestinationPath());
                        }
                    }
                }

                stopwatch.Stop();
                _logger.LogInformation($"Copy from ms to fs = {stopwatch.Elapsed.TotalMilliseconds}");
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, nameof(WriteToFileUsingMemoryStream));
                _zipFileDownloadService.NotifyDownloadComplete(null);
            }
        }

        public async Task HandleZipAllContentMemoryBasedOld(MultipartSection zipContentSection)
        {
            try
            {
                _logger.LogInformation($"{nameof(HandleZipAllContentMemoryBasedOld)}");
                if (zipContentSection == null)
                {
                    throw new ArgumentException(nameof(zipContentSection));
                }

                var memoryStream = new MemoryStream();
                await zipContentSection.Body.CopyToAsync(memoryStream);
                _logger.LogInformation($"Read into memory = {memoryStream.Length}");
                var tIgnore = Task.Run(() => WriteToFileUsingMemoryStream(memoryStream));
                _logger.LogInformation($"Scheduled copy to FileStream");
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, nameof(HandleZipAllContentMemoryBasedOld));
                _zipFileDownloadService.NotifyDownloadComplete(null);
            }
        }

        private async Task WriteToFileUsingStream(Stream stream)
        {
            try
            {
                var stopwatch = Stopwatch.StartNew();
                _logger.LogInformation($"BBB Writing to file stream from ms");

                var buffer = new byte[8192];
                bool isMoreToRead = true;
                long totalBytesRead = 0;
                int readCount = 0;

                using (var fs = new FileStream(GetZipDestinationPath(), FileMode.CreateNew,
                    FileAccess.Write))
                {
                    do
                    {
                        var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                        if (bytesRead == 0)
                        {
                            isMoreToRead = false;
                            TriggerProgressChanged(_totalBytes, totalBytesRead);
                            continue;
                        }

                        await fs.WriteAsync(buffer, 0, bytesRead);

                        totalBytesRead += bytesRead;
                        readCount += 1;

                        if (readCount % 100 == 0)
                        {
                            TriggerProgressChanged(_totalBytes, totalBytesRead);
                        }
                    }
                    while (isMoreToRead);

                    _logger.LogInformation("All bytes written. Signalling download complete");
                    _zipFileDownloadService.NotifyDownloadComplete(GetZipDestinationPath());
                }

                stopwatch.Stop();
                _logger.LogInformation($"Copy from ms to fs = {stopwatch.Elapsed.TotalMilliseconds}");
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, nameof(WriteToFileUsingStream));
                _zipFileDownloadService.NotifyDownloadComplete(null);
            }
        }

        private void TriggerProgressChanged(long totalDownloadSize, long totalBytesRead)
        {
            double progressPercentage = Math.Round((double)totalBytesRead / totalDownloadSize * 100, 2);
            _logger.LogInformation($"[Progress] TotalSize = {totalDownloadSize} ReadSoFar = {totalBytesRead} % = {progressPercentage}");
        }

        public async Task HandleZipAllContentMemoryBased(MultipartSection zipContentSection)
        {
            try
            {
                _logger.LogInformation($"{nameof(HandleZipAllContentMemoryBased)}");
                if (zipContentSection == null)
                {
                    throw new ArgumentException(nameof(zipContentSection));
                }

                // var tIgnore = Task.Run(() => WriteToFileUsingStream(zipContentSection.Body));
                await WriteToFileUsingStream(zipContentSection.Body);
                _logger.LogInformation($"Scheduled copy to FileStream");
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, nameof(HandleZipAllContentMemoryBased));
                _zipFileDownloadService.NotifyDownloadComplete(null);
            }
        }

        private bool AllBytesRead(long bytesRead)
        {
            if (_totalBytes == long.MinValue)
            {
                throw new InvalidOperationException(
                    $"Attempting to write {bytesRead} bytes to uninitialized bytes");
            }

            if (_totalBytes == bytesRead)
            {
                _logger.LogInformation($"All bytes read = {_totalBytes}");
                return true;
            }
            else
            {
                _logger.LogInformation($"Expected bytes = {_totalBytes} Actual = {bytesRead}");
                return false;
            }
        }
    }
}
