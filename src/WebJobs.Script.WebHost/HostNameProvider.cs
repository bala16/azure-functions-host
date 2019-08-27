// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Azure.AppService.Proxy.Common.Environment;
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
                _logger.LogInformation("Current HostNameProvider Value = " + _hostName);
                if (string.IsNullOrEmpty(_hostName))
                {
                    // default to the the value specified in environment
                    _hostName = _environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteHostName);
                    _logger.LogInformation("setting HostNameProvider _hostName = " + _hostName);
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
                return _hostName;
            }
        }

        public virtual void Synchronize(HttpRequest request)
        {
            _logger.LogInformation("synchronize Host " + request.Host + " DisplayUrl " + request.GetDisplayUrl());

            string runtimeSiteName = _environment.GetEnvironmentVariable(EnvironmentSettingNames.RuntimeSiteName);

            string hostNameHeaderValue = request.Headers[ScriptConstants.AntaresDefaultHostNameHeader];
            _logger.LogInformation("synchronize hostNameHeaderValue = " + hostNameHeaderValue + " value = " + Value + " runtimeSiteName " + runtimeSiteName);

            string siteDeploymentId = request.Headers[ScriptConstants.AntaresSiteDeploymentId];
            _logger.LogInformation("synchronize SiteDeploymentId = " + siteDeploymentId + " value " + Value + " runtimeSiteName " + runtimeSiteName);

            if (!string.IsNullOrEmpty(hostNameHeaderValue) &&
                string.Compare(Value, hostNameHeaderValue) != 0)
            {
                if (_environment.IsLinuxAppServiceEnvironment())
                {
                    // 2 cases
                    // 1 . container hasn't been specialized yet

                    if (string.IsNullOrEmpty(runtimeSiteName))
                    {
                        _logger.LogInformation("string.IsNullOrEmpty(runtimeSiteName)");
                        _logger.LogInformation("Skip update HostName from '{0}' to '{1}' CurrentRuntimeSite '{2}' DeploymenyId '{3}'", Value, hostNameHeaderValue, runtimeSiteName, siteDeploymentId);
                        return;
                    }

                    if (!string.Equals(runtimeSiteName, siteDeploymentId, StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogInformation("!string.Equals(runtimeSiteName, siteDeploymentId, StringComparison.OrdinalIgnoreCase)");
                        _logger.LogInformation("Skip update HostName from '{0}' to '{1}' CurrentRuntimeSite '{2}' DeploymenyId '{3}'", Value, hostNameHeaderValue, runtimeSiteName, siteDeploymentId);
                        return;
                    }
                }

                if (string.Compare(Value, hostNameHeaderValue) != 0)
                    {
                        _logger.LogInformation("HostName updated from '{0}' to '{1}'", Value, hostNameHeaderValue);
                        _hostName = hostNameHeaderValue;
                    }
            }
        }

        internal void Reset()
        {
            _hostName = null;
        }
    }
}
