// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Azure.AppService.Proxy.Common.Constants;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Azure.WebJobs.Script.WebHost.Security;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.WebHost.ContainerManagement
{
    public class StreamerService
    {
        private readonly ZipFileDownloadService _zipFileDownloadService;
        private readonly ILogger<StreamerService> _logger;
        private readonly IEnvironment _environment;
        private readonly ReaderWriterLockSlim _readerWriterLockSlim;
        private long _totalBytes = long.MinValue;
        private long _bytesReadSoFar = 0;

        public StreamerService(ZipFileDownloadService zipFileDownloadService, ILogger<StreamerService> logger, IEnvironment environment)
        {
            _zipFileDownloadService = zipFileDownloadService;
            _logger = logger;
            _environment = environment;
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

        public async Task HandleZipContentOld(MultipartSection zipContentSection)
        {
            try
            {
                _logger.LogInformation($"{nameof(HandleZipContent)}");
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
                _logger.LogWarning(e, nameof(HandleZipContentOld));
                _zipFileDownloadService.NotifyDownloadComplete(null);
            }
        }

        public async Task HandleZipContent(MultipartSection zipContentSection)
        {
            try
            {
                _logger.LogInformation($"{nameof(HandleZipContent)}");
                if (zipContentSection == null)
                {
                    throw new ArgumentException(nameof(zipContentSection));
                }

                using (var memoryStream = new MemoryStream())
                {
                    await zipContentSection.Body.CopyToAsync(memoryStream);
                    var length = memoryStream.Length;

                    _logger.LogInformation(
                        $"BBB {DateTime.UtcNow} Section len bytes = {zipContentSection.Body.Length} memoryStream.Length = {length}");

                    memoryStream.Seek(0, SeekOrigin.Begin);
                    var s = Encoding.UTF8.GetString(memoryStream.ToArray());
                    var environmentVariable = _environment.GetEnvironmentVariable(EnvironmentSettingNames.WebSiteAuthEncryptionKey);
                    var decrypt = SimpleWebTokenHelper.DecryptBytes(environmentVariable.ToKeyBytes(), s);

                    using (var fs = new FileStream(GetZipDestinationPath(), FileMode.Append,
                        FileAccess.Write))
                    {
                        await fs.WriteAsync(decrypt);

                        fs.Flush(); // flush once at the end?

                        _logger.LogInformation(
                            $"BBB {DateTime.UtcNow} Decrypted len = {decrypt.Length} fs so far len = {fs.Length}");

                        if (AllBytesRead(decrypt.Length))
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

        public async Task HandleZipContentWithoutEncryption(MultipartSection zipContentSection)
        {
            try
            {
                _logger.LogInformation($"{nameof(HandleZipContentWithoutEncryption)}");
                if (zipContentSection == null)
                {
                    throw new ArgumentException(nameof(zipContentSection));
                }

                using (var fs = new FileStream(GetZipDestinationPath(), FileMode.Append,
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
                _logger.LogWarning(e, nameof(HandleZipContentWithoutEncryption));
                _zipFileDownloadService.NotifyDownloadComplete(null);
            }
        }
    }
}
