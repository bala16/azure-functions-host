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
    public class FunctionsFolderService
    {
        private readonly ILogger<FunctionsFolderService> _logger;
        private readonly FunctionsFolderNotificationService _functionsFolderNotificationService;

        public FunctionsFolderService(ILogger<FunctionsFolderService> logger, FunctionsFolderNotificationService functionsFolderNotificationService)
        {
            _logger = logger;
            _functionsFolderNotificationService = functionsFolderNotificationService;
        }

        public async Task<HydrateFolderRequest> GetMetadata(MultipartSection section)
        {
            _logger.LogInformation($"Start {nameof(GetMetadata)}");
            if (section == null)
            {
                throw new ArgumentException(nameof(section));
            }

            using (var streamReader = new StreamReader(section.Body))
            {
                string metadataContent = await streamReader.ReadToEndAsync();
                return JsonConvert.DeserializeObject<HydrateFolderRequest>(metadataContent);
            }
        }

        public async Task HandleZip(MultipartSection zipContentSection, HydrateFolderRequest hydrateFolderRequest)
        {
            var tempFileName = Path.GetTempFileName();
            _logger.LogInformation($"Start {nameof(HandleZip)}. TempFileName = {tempFileName}");
            if (zipContentSection == null)
            {
                throw new ArgumentException(nameof(zipContentSection));
            }
            _functionsFolderNotificationService.NotifyDownloadStart();

            using (var fs = new FileStream(tempFileName, FileMode.Create, FileAccess.Write))
            {
                await zipContentSection.Body.CopyToAsync(fs);
                _logger.LogInformation($"Total bytes written = {fs.Length}");
                fs.Flush();
            }

            _functionsFolderNotificationService.NotifyDownloadComplete(tempFileName);
        }
    }
}
