// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Azure.WebJobs.Script.WebHost.ContainerManagement;
using Microsoft.Azure.WebJobs.Script.WebHost.Filters;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authorization.Policies;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Controllers
{
    /// <summary>
    /// Controller responsible for instance operations that are orthogonal to the script host.
    /// An instance is an unassigned generic container running with the runtime in standby mode.
    /// These APIs are used by the AppService Controller to validate standby instance status and info.
    /// </summary>
    public class InstanceController : Controller
    {
        private readonly IEnvironment _environment;
        private readonly IInstanceManager _instanceManager;
        private readonly ILogger _logger;
        private readonly StartupContextProvider _startupContextProvider;
        private readonly ZipFileDownloadService _zipFileDownloadService;
        private static readonly List<string> Messages = new List<string>();

        public InstanceController(IEnvironment environment, IInstanceManager instanceManager, ILoggerFactory loggerFactory, StartupContextProvider startupContextProvider, ZipFileDownloadService zipFileDownloadService)
        {
            _environment = environment;
            _instanceManager = instanceManager;
            _logger = loggerFactory.CreateLogger<InstanceController>();
            _startupContextProvider = startupContextProvider;
            _zipFileDownloadService = zipFileDownloadService;
        }

        [HttpPost]
        [Route("admin/instance/assign")]
        [Authorize(Policy = PolicyNames.AdminAuthLevel)]
        public async Task<IActionResult> Assign([FromBody] EncryptedHostAssignmentContext encryptedAssignmentContext)
        {
            _logger.LogDebug($"Starting container assignment for host : {Request?.Host}. ContextLength is: {encryptedAssignmentContext.EncryptedContext?.Length}");
            Messages.Add("Starting admin/instance/assign");

            var assignmentContext = _startupContextProvider.SetContext(encryptedAssignmentContext);

            // before starting the assignment we want to perform as much
            // up front validation on the context as possible
            string error = await _instanceManager.ValidateContext(assignmentContext);
            if (error != null)
            {
                return StatusCode(StatusCodes.Status400BadRequest, error);
            }

            // Wait for Sidecar specialization to complete before returning ok.
            // This shouldn't take too long so ok to do this sequentially.
            error = await _instanceManager.SpecializeMSISidecar(assignmentContext);
            if (error != null)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, error);
            }

            var succeeded = _instanceManager.StartAssignment(assignmentContext);

            Messages.Add($"admin/instance/assign response = {succeeded}");

            return succeeded
                ? Accepted()
                : StatusCode(StatusCodes.Status409Conflict, "Instance already assigned");
        }

        [HttpGet]
        [Route("admin/instance/info")]
        [Authorize(Policy = PolicyNames.AdminAuthLevel)]
        public IActionResult GetInstanceInfo()
        {
            return Ok(_instanceManager.GetInstanceInfo());
        }

        [HttpPost]
        [Route("admin/instance/disable")]
        [Authorize(Policy = PolicyNames.AdminAuthLevel)]
        public async Task<IActionResult> Disable([FromServices] IScriptHostManager hostManager)
        {
            _logger.LogDebug("Disabling container");
            // Mark the container disabled. We check for this on host restart
            await Utility.MarkContainerDisabled(_logger);
            var tIgnore = Task.Run(() => hostManager.RestartHostAsync());
            return Ok();
        }

        [HttpGet]
        [Route("admin/instance/http-health")]
        public IActionResult GetHttpHealthStatus()
        {
            // Reaching here implies that http health of the container is ok.
            return Ok();
        }

        [HttpGet]
        [Route("admin/instance/get-logs")]
        public string GetLogs()
        {
            var stringBuilder = new StringBuilder();
            foreach (var message in Messages)
            {
                stringBuilder.Append(message);
                stringBuilder.Append(Environment.NewLine);
            }

            return stringBuilder.ToString();
        }

        private static string GetZipDestinationPath(string zipFileName)
        {
            var tmpPath = Path.GetTempPath();
            var fileName = Path.GetFileName(zipFileName);
            var filePath = Path.Combine(tmpPath, zipFileName);
            return filePath;
        }

        [HttpPost]
        [Route("admin/instance/stream-zip-single")]
        [DisableFormValueModelBinding]
        public async Task<IActionResult> StreamSingle([FromServices] SingleStreamerService streamerService)
        {
            _logger.LogInformation($"BBB Invoked {nameof(StreamSingle)}");

            var stopwatch = Stopwatch.StartNew();
            bool success = true;

            try
            {
                var boundary = GetBoundary(
                    MediaTypeHeaderValue.Parse(Request.ContentType),
                    new FormOptions().MultipartBoundaryLengthLimit);

                var reader = new MultipartReader(boundary, HttpContext.Request.Body);

                var metadataSection = await reader.ReadNextSectionAsync();
                await streamerService.HandleMetadata(metadataSection);

                var zipContentSection = await reader.ReadNextSectionAsync();
                // await streamerService.HandleZipAllContent(zipContentSection);
                await streamerService.HandleZipAllContentMemoryBased(zipContentSection);

                success = true;

                return Ok();
            }
            catch (Exception e)
            {
                success = false;
                _logger.LogWarning(e, nameof(StreamSingle));
                return BadRequest(e.ToString());
            }
            finally
            {
                stopwatch.Stop();
                _logger.LogInformation($"Total time taken = {stopwatch.Elapsed.TotalMilliseconds} Result = {success}");
            }
        }

        [HttpPost]
        [Route("admin/instance/stream-zip")]
        [DisableFormValueModelBinding]
        public async Task<IActionResult> Stream([FromServices] StreamerService streamerService)
        {
            _logger.LogInformation($"BBB Invoked {nameof(Stream)}");

            try
            {
                var boundary = GetBoundary(
                    MediaTypeHeaderValue.Parse(Request.ContentType),
                    new FormOptions().MultipartBoundaryLengthLimit);

                var reader = new MultipartReader(boundary, HttpContext.Request.Body);

                var metadataSection = await reader.ReadNextSectionAsync();
                await streamerService.HandleMetadata(metadataSection);

                var zipContentSection = await reader.ReadNextSectionAsync();
                // await streamerService.HandleZipContent(zipContentSection);
                await streamerService.HandleZipContentWithoutEncryption(zipContentSection);

                return Ok();
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, nameof(Stream));
                return BadRequest(e.ToString());
            }
        }

        [HttpPost]
        [Route("admin/instance/stream-zip-old")]
        public async Task<IActionResult> StreamOld()
        {
            string filePath = GetZipDestinationPath("download.zip");
            _logger.LogInformation($"BBB Zip cache downloading to {filePath}");

            try
            {
                var boundary = GetBoundary(
                    MediaTypeHeaderValue.Parse(Request.ContentType),
                    new FormOptions().MultipartBoundaryLengthLimit);

                var reader = new MultipartReader(boundary, HttpContext.Request.Body);

                _logger.LogInformation("BBB Reading metadata");

                var section1 = await reader.ReadNextSectionAsync();
                var streamReader = new StreamReader(section1.Body);
                string content1 = await streamReader.ReadToEndAsync();
                var zipMetadata = JsonConvert.DeserializeObject<ZipMetadata>(content1);
                _logger.LogInformation($"BBB ZipMetadata read = {zipMetadata}");

                _logger.LogInformation("BBB Reading section 2");

                var section2 = await reader.ReadNextSectionAsync();

                using (var memoryStream = new MemoryStream())
                {
                    await section2.Body.CopyToAsync(memoryStream);

                    using (var fs = new FileStream(filePath, FileMode.Append,
                        FileAccess.Write))
                    {
                        _logger.LogInformation($"BBB {DateTime.UtcNow} Writing to file stream chunk");

                        memoryStream.Seek(0, SeekOrigin.Begin);
                        _logger.LogInformation($"BBB Writing {memoryStream.Length} bytes");
                        await memoryStream.CopyToAsync(fs);
                        fs.Flush();
                    }
                }

                _logger.LogInformation($"BBB Returning Ok. Downloaded at {filePath}");
                _zipFileDownloadService.NotifyDownloadComplete(filePath);
                return Ok();
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                var stringBuilder = new StringBuilder();
                stringBuilder.Append(e);
                stringBuilder.Append(Environment.NewLine);
                stringBuilder.Append($"Download failed");
                _zipFileDownloadService.NotifyDownloadComplete(string.Empty);
                return BadRequest(stringBuilder.ToString());
            }
        }

        [HttpGet]
        [Route("admin/instance/get-info")]
        public string In()
        {
            try
            {
                var stringBuilder = new StringBuilder();
                stringBuilder.AppendLine(
                    $"Container name = {Environment.GetEnvironmentVariable(EnvironmentSettingNames.ContainerName)}");

                string filePath = GetZipDestinationPath("download.zip");

                stringBuilder.AppendLine($"Path = {filePath}");

                var fileInfo = new FileInfo(filePath);

                stringBuilder.AppendLine($"Length = {fileInfo.Length}");
                stringBuilder.AppendLine($"Name = {fileInfo.Name}");
                stringBuilder.AppendLine($"Directory = {fileInfo.Directory}");
                stringBuilder.AppendLine($"DirectoryName = {fileInfo.DirectoryName}");
                stringBuilder.AppendLine($"FullName = {fileInfo.FullName}");
                stringBuilder.AppendLine($"CreationTime = {fileInfo.CreationTime}");
                stringBuilder.AppendLine($"LastWriteTime = {fileInfo.LastWriteTime}");

                return stringBuilder.ToString();
            }
            catch (Exception e)
            {
                return e.ToString();
            }
        }

        [HttpGet]
        [Route("admin/instance/list-home")]
        public Task<string> GetList()
        {
            var stringBuilder = new StringBuilder();
            foreach (var enumerateFileSystemEntry in Directory.EnumerateFileSystemEntries("/home"))
            {
                stringBuilder.Append(enumerateFileSystemEntry);
                stringBuilder.Append(Environment.NewLine);
            }

            return Task.FromResult(stringBuilder.ToString());
        }

        private static string GetBoundary(MediaTypeHeaderValue contentType, int lengthLimit)
        {
            var boundary = HeaderUtilities.RemoveQuotes(contentType.Boundary).Value;

            if (string.IsNullOrWhiteSpace(boundary))
            {
                throw new InvalidDataException("Missing content-type boundary.");
            }

            if (boundary.Length > lengthLimit)
            {
                throw new InvalidDataException(
                    $"Multipart boundary length limit {lengthLimit} exceeded.");
            }

            return boundary;
        }
    }
}
