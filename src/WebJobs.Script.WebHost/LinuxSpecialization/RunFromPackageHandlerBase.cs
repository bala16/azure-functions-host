// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost.LinuxSpecialization
{
    public abstract class RunFromPackageHandlerBase : IRunFromPackageHandler
    {
        private readonly IEnvironment _environment;
        private readonly IOptionsFactory<ScriptApplicationHostOptions> _optionsFactory;
        private readonly HttpClient _client;
        private readonly IMeshServiceClient _meshServiceClient;
        private readonly IMetricsLogger _metricsLogger;
        private readonly ILogger<RunFromPackageHandlerBase> _logger;

        protected RunFromPackageHandlerBase(IEnvironment environment,
            IOptionsFactory<ScriptApplicationHostOptions> optionsFactory, HttpClient client,
            IMeshServiceClient meshServiceClient, IMetricsLogger metricsLogger, ILogger<RunFromPackageHandlerBase> logger)
        {
            _environment = environment;
            _optionsFactory = optionsFactory;
            _client = client;
            _meshServiceClient = meshServiceClient;
            _metricsLogger = metricsLogger;
            _logger = logger;
            _logger.LogInformation($"ctor {nameof(RunFromPackageHandlerBase)}");
        }

        public abstract Task<bool> DeployToLocalDisk(HostAssignmentContext assignmentContext, RunFromPackageContext pkgContext);

        public abstract Task<bool> DeployToAzureFiles(HostAssignmentContext assignmentContext, RunFromPackageContext pkgContext);

        protected virtual async Task<bool> ApplyBlobPackageContext(HostAssignmentContext assignmentContext, RunFromPackageContext pkgContext)
        {
            _logger.LogInformation($"Start {nameof(ApplyBlobPackageContext)}");

            // We need to get the non-PlaceholderMode script Path so we can unzip to the correct location.
            // This asks the factory to skip the PlaceholderMode check when configuring options.
            var options = _optionsFactory.Create(ScriptApplicationHostOptionsSetup.SkipPlaceholder);
            var targetPath = options.ScriptPath;

            // download zip and extract
            var filePath = await Download(pkgContext);
            await UnpackPackage(filePath, targetPath, pkgContext);

            string bundlePath = Path.Combine(targetPath, "worker-bundle");
            if (Directory.Exists(bundlePath))
            {
                _logger.LogInformation($"Python worker bundle detected");
            }

            return true;
        }

        public async Task<string> Download(RunFromPackageContext pkgContext)
        {
            _logger.LogInformation($"Start {nameof(Download)}");

            var zipUri = new Uri(pkgContext.Url);
            if (!Utility.TryCleanUrl(zipUri.AbsoluteUri, out string cleanedUrl))
            {
                throw new Exception("Invalid url for the package");
            }

            var tmpPath = Path.GetTempPath();
            var fileName = Path.GetFileName(zipUri.AbsolutePath);
            var filePath = Path.Combine(tmpPath, fileName);
            if (pkgContext.PackageContentLength != null && pkgContext.PackageContentLength > 100 * 1024 * 1024)
            {
                _logger.LogInformation($"Downloading zip contents from '{cleanedUrl}' using aria2c'");
                AriaDownload(tmpPath, fileName, zipUri, pkgContext.IsWarmUpRequest);
            }
            else
            {
                _logger.LogInformation($"Downloading zip contents from '{cleanedUrl}' using httpclient'");
                await HttpClientDownload(filePath, zipUri, pkgContext.IsWarmUpRequest);
            }

            return filePath;
        }

        private void AriaDownload(string directory, string fileName, Uri zipUri, bool isWarmupRequest)
        {
            var metricName = isWarmupRequest
                ? MetricEventNames.LinuxContainerSpecializationZipDownloadWarmup
                : MetricEventNames.LinuxContainerSpecializationZipDownload;
            (string stdout, string stderr, int exitCode) = RunBashCommand(
                $"aria2c --allow-overwrite -x12 -d {directory} -o {fileName} '{zipUri}'",
                metricName);
            if (exitCode != 0)
            {
                var msg = $"Error downloading package. stdout: {stdout}, stderr: {stderr}, exitCode: {exitCode}";
                _logger.LogError(msg);
                throw new InvalidOperationException(msg);
            }
            var fileInfo = FileUtility.FileInfoFromFileName(Path.Combine(directory, fileName));
            _logger.LogInformation($"{fileInfo.Length} bytes downloaded. IsWarmupRequest = {isWarmupRequest}");
        }

        private async Task HttpClientDownload(string filePath, Uri zipUri, bool isWarmupRequest)
        {
            HttpResponseMessage response = null;
            await Utility.InvokeWithRetriesAsync(async () =>
            {
                try
                {
                    var downloadMetricName = isWarmupRequest
                        ? MetricEventNames.LinuxContainerSpecializationZipDownloadWarmup
                        : MetricEventNames.LinuxContainerSpecializationZipDownload;
                    using (_metricsLogger.LatencyEvent(downloadMetricName))
                    {
                        var request = new HttpRequestMessage(HttpMethod.Get, zipUri);
                        response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                        response.EnsureSuccessStatusCode();
                    }
                }
                catch (Exception e)
                {
                    string error = $"Error downloading zip content";
                    _logger.LogError(e, error);
                    throw;
                }
                _logger.LogInformation($"{response.Content.Headers.ContentLength} bytes downloaded. IsWarmupRequest = {isWarmupRequest}");
            }, 2, TimeSpan.FromSeconds(0.5));

            using (_metricsLogger.LatencyEvent(isWarmupRequest ? MetricEventNames.LinuxContainerSpecializationZipWriteWarmup : MetricEventNames.LinuxContainerSpecializationZipWrite))
            {
                using (var content = await response.Content.ReadAsStreamAsync())
                using (var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 4096, useAsync: true))
                {
                    await content.CopyToAsync(stream);
                }
                _logger.LogInformation($"{response.Content.Headers.ContentLength} bytes written. IsWarmupRequest = {isWarmupRequest}");
            }
        }

        protected async Task UnpackPackage(string filePath, string scriptPath, RunFromPackageContext pkgContext)
        {
            CodePackageType packageType;
            using (_metricsLogger.LatencyEvent(MetricEventNames.LinuxContainerSpecializationGetPackageType))
            {
                packageType = GetPackageType(filePath, pkgContext);
            }

            if (packageType == CodePackageType.Squashfs)
            {
                // default to mount for squashfs images
                if (_environment.IsMountDisabled())
                {
                    UnsquashImage(filePath, scriptPath);
                }
                else
                {
                    await _meshServiceClient.MountFuse("squashfs", filePath, scriptPath);
                }
            }
            else if (packageType == CodePackageType.Zip)
            {
                // default to unzip for zip packages
                if (_environment.IsMountEnabled())
                {
                    await _meshServiceClient.MountFuse("zip", filePath, scriptPath);
                }
                else
                {
                    UnzipPackage(filePath, scriptPath);
                }
            }
        }

        private void UnzipPackage(string filePath, string scriptPath)
        {
            using (_metricsLogger.LatencyEvent(MetricEventNames.LinuxContainerSpecializationZipExtract))
            {
                _logger.LogInformation($"Extracting files to '{scriptPath}'");
                ZipFile.ExtractToDirectory(filePath, scriptPath, overwriteFiles: true);
                _logger.LogInformation($"Zip extraction complete");
            }
        }

        private CodePackageType GetPackageType(string filePath, RunFromPackageContext pkgContext)
        {
            // cloud build always builds squashfs
            if (pkgContext.IsScmRunFromPackage())
            {
                return CodePackageType.Squashfs;
            }

            var uri = new Uri(pkgContext.Url);
            // check file name since it'll be faster than running `file`
            if (FileIsAny(".squashfs", ".sfs", ".sqsh", ".img", ".fs"))
            {
                return CodePackageType.Squashfs;
            }
            else if (FileIsAny(".zip"))
            {
                return CodePackageType.Zip;
            }

            // Check file magic-number using `file` command.
            (var output, _, _) = RunBashCommand($"file -b {filePath}", MetricEventNames.LinuxContainerSpecializationFileCommand);
            if (output.StartsWith("Squashfs", StringComparison.OrdinalIgnoreCase))
            {
                return CodePackageType.Squashfs;
            }
            else if (output.StartsWith("Zip", StringComparison.OrdinalIgnoreCase))
            {
                return CodePackageType.Zip;
            }
            else
            {
                throw new InvalidOperationException($"Can't find CodePackageType to match {filePath}");
            }

            bool FileIsAny(params string[] options)
                => options.Any(o => uri.AbsolutePath.EndsWith(o, StringComparison.OrdinalIgnoreCase));
        }

        protected void UnsquashImage(string filePath, string scriptPath)
            => RunBashCommand($"unsquashfs -f -d '{scriptPath}' '{filePath}'", MetricEventNames.LinuxContainerSpecializationUnsquash);

        protected (string, string, int) RunBashCommand(string command, string metricName)
        {
            try
            {
                using (_metricsLogger.LatencyEvent(metricName))
                {
                    var process = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "bash",
                            Arguments = $"-c \"{command}\"",
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }
                    };
                    _logger.LogInformation($"Running: {process.StartInfo.FileName} {process.StartInfo.Arguments}");
                    process.Start();
                    var output = process.StandardOutput.ReadToEnd().Trim();
                    var error = process.StandardError.ReadToEnd().Trim();
                    process.WaitForExit();
                    _logger.LogInformation($"Output: {output}");
                    if (process.ExitCode != 0)
                    {
                        _logger.LogError(error);
                    }
                    else
                    {
                        _logger.LogInformation($"error: {error}");
                    }
                    _logger.LogInformation($"exitCode: {process.ExitCode}");
                    return (output, error, process.ExitCode);
                }
            }
            catch (Exception e)
            {
                _logger.LogError("Error running bash", e);
            }

            return (string.Empty, string.Empty, -1);
        }
    }
}
