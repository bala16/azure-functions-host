﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    /// <summary>
    /// Provides the current HostName for the Function App.
    /// <remarks>
    /// The environment value for WEBSITE_HOSTNAME is unreliable and shouldn't be used directly. AppService site swaps change
    /// the site’s hostname under the covers, and the worker process is NOT recycled (for performance reasons). That means the
    /// site will continue to run with the same hostname environment variable, leading to an incorrect host name.
    ///
    /// WAS_DEFAULT_HOSTNAME is a header injected by front end on every request which provides the correct hostname. We check
    /// this header on all http requests, and updated the cached hostname value as needed.
    /// </remarks>
    /// </summary>
    public class HostNameProvider
    {
        private readonly IEnvironment _environment;
        private readonly ILogger _logger;
        private string _hostName;

        public HostNameProvider(IEnvironment environment, ILogger<HostNameProvider> logger)
        {
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _logger = logger;
        }

        public virtual string Value
        {
            get
            {
                if (string.IsNullOrEmpty(_hostName))
                {
                    // default to the the value specified in environment
                    _hostName = _environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteHostName);
                    _logger.LogInformation("Setting hostname = ", _hostName);

                    if (string.IsNullOrEmpty(_hostName))
                    {
                        // Linux Dedicated on AppService doesn't have WEBSITE_HOSTNAME
                        string websiteName = _environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName);
                        if (!string.IsNullOrEmpty(websiteName))
                        {
                            _hostName = $"{websiteName}.azurewebsites.net";
                        }
                    }
                }
                _logger.LogInformation("Current hostname = ", _hostName);
                return _hostName;
            }
        }

        public virtual void Synchronize(HttpRequest request)
        {
            string hostNameHeaderValue = request.Headers[ScriptConstants.AntaresDefaultHostNameHeader];
            if (!string.IsNullOrEmpty(hostNameHeaderValue) &&
                string.Compare(Value, hostNameHeaderValue) != 0)
            {
                    if (string.Compare(Value, hostNameHeaderValue) != 0)
                    {
                        // Restrict this to Linux consumption for now.
                        if (_environment.IsLinuxContainerEnvironment())
                        {
                            string runtimeSiteName = _environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName);
                            string siteDeploymentId = request.Headers[ScriptConstants.AntaresSiteDeploymentId];

                            // There are 2 scenarios the hostname shouldn't be updated.
                            // 1. current container hasn't been specialized yet and the incoming request is for a specific site.
                            // 2. current container is already assigned to a site and the incoming request is for a different site.

                            if (string.IsNullOrEmpty(runtimeSiteName) ||
                                !string.Equals(runtimeSiteName, siteDeploymentId, StringComparison.OrdinalIgnoreCase))
                            {
                                _logger.LogInformation("Skip update HostName from '{0}' to '{1}' CurrentRuntimeSite '{2}' DeploymentId '{3}'", Value, hostNameHeaderValue, runtimeSiteName, siteDeploymentId);
                                return;
                            }
                        }

                        _logger.LogInformation("HostName updated from '{0}' to '{1}'", Value, hostNameHeaderValue);
                        _hostName = hostNameHeaderValue;
                    }
            }
            else if (string.IsNullOrEmpty(hostNameHeaderValue))
            {
                _logger.LogInformation("string.IsNullOrEmpty(hostNameHeaderValue)");
            }
            else
            {
                _logger.LogInformation("Now hostNameHeaderValue = " + hostNameHeaderValue);
            }
        }

        internal void Reset()
        {
            _hostName = null;
        }
    }
}
