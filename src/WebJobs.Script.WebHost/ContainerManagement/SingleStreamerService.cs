// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
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
