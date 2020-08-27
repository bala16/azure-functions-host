// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.WebHost.ContainerManagement
{
    public class StreamerService
    {
        private readonly ZipFileDownloadService _zipFileDownloadService;
        private readonly ILogger<StreamerService> _logger;
        private readonly ReaderWriterLockSlim _readerWriterLockSlim;
        private long _totalBytes = long.MinValue;
        private long _bytesReadSoFar = 0;

        public StreamerService(ZipFileDownloadService zipFileDownloadService, ILogger<StreamerService> logger)
        {
            _zipFileDownloadService = zipFileDownloadService;
            _logger = logger;
            _readerWriterLockSlim = new ReaderWriterLockSlim();
        }

        private static string GetZipDestinationPath()
        {
            var fileName = Environment.GetEnvironmentVariable(EnvironmentSettingNames.ContainerName) ?? "zip-stream";
            var tmpPath = Path.GetTempPath();
            return Path.Combine(tmpPath, $"{fileName}.zip");
        }

        private void SetTotalBytes(long bytes)
        {
            if (_readerWriterLockSlim.TryEnterWriteLock(TimeSpan.FromMilliseconds(10)))
            {
                try
                {
                    if (_totalBytes != long.MinValue && _totalBytes != bytes)
                    {
                        throw new InvalidOperationException(
                            $"Attempt reinitialize total bytes from {_totalBytes} to {bytes}");
                    }

                    _logger.LogInformation($"Initializing total bytes to {bytes}");
                    _totalBytes = bytes;
                }
                finally
                {
                    _readerWriterLockSlim.ExitWriteLock();
                }
            }
            else
            {
                throw new TimeoutException(nameof(SetTotalBytes));
            }
        }

        private bool AllBytesRead(long bytesRead)
        {
            if (_readerWriterLockSlim.TryEnterWriteLock(TimeSpan.FromMilliseconds(10)))
            {
                try
                {
                    if (_totalBytes == long.MinValue)
                    {
                        throw new InvalidOperationException(
                            $"Attempting to write {bytesRead} bytes to uninitialized bytes");
                    }

                    _bytesReadSoFar += bytesRead;
                    _logger.LogInformation($"{_bytesReadSoFar}/{_totalBytes} bytes read so far");

                    if (_bytesReadSoFar == _totalBytes)
                    {
                        return true;
                    }
                    else if (_bytesReadSoFar < _totalBytes)
                    {
                        return false;
                    }
                    else
                    {
                        throw new InvalidOperationException($"Reading {_bytesReadSoFar} bytes / {_totalBytes} bytes");
                    }
                }
                finally
                {
                    _readerWriterLockSlim.ExitWriteLock();
                }
            }
            else
            {
                throw new TimeoutException(nameof(SetTotalBytes));
            }
        }

        public async Task HandleMetadata(MultipartSection section)
        {
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

        public async Task HandleZipContent(MultipartSection zipContentSection)
        {
            try
            {
                if (zipContentSection == null)
                {
                    throw new ArgumentException(nameof(zipContentSection));
                }

                using (var memoryStream = new MemoryStream())
                {
                    await zipContentSection.Body.CopyToAsync(memoryStream);

                    using (var fs = new FileStream(GetZipDestinationPath(), FileMode.Append,
                        FileAccess.Write))
                    {
                        _logger.LogInformation($"BBB {DateTime.UtcNow} Writing to file stream chunk");

                        memoryStream.Seek(0, SeekOrigin.Begin);
                        _logger.LogInformation($"BBB Writing {memoryStream.Length} bytes");
                        await memoryStream.CopyToAsync(fs);
                        fs.Flush(); // flush once at the end?

                        if (AllBytesRead(memoryStream.Length))
                        {
                            _logger.LogInformation("All bytes read. Signalling download complete");
                            _zipFileDownloadService.NotifyDownloadComplete(GetZipDestinationPath());
                        }
                    }
                }
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, nameof(HandleZipContent));
                _zipFileDownloadService.NotifyDownloadComplete(null);
            }
        }
    }
}
