// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Configuration
{
    public class ScriptApplicationHostOptionsSetup : IConfigureNamedOptions<ScriptApplicationHostOptions>, IDisposable
    {
        public const string SkipPlaceholder = "SkipPlaceholder";
        private readonly IOptionsMonitorCache<ScriptApplicationHostOptions> _cache;
        private readonly IConfiguration _configuration;
        private readonly IOptionsMonitor<StandbyOptions> _standbyOptions;
        private readonly IDisposable _standbyOptionsOnChangeSubscription;

        public ScriptApplicationHostOptionsSetup(IConfiguration configuration, IOptionsMonitor<StandbyOptions> standbyOptions,
            IOptionsMonitorCache<ScriptApplicationHostOptions> cache)
        {
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _standbyOptions = standbyOptions ?? throw new ArgumentNullException(nameof(standbyOptions));

            // If standby options change, invalidate this options cache.
            _standbyOptionsOnChangeSubscription = _standbyOptions.OnChange(o => _cache.Clear());
        }

        private static void Log(string message)
        {
            var containerName = Environment.GetEnvironmentVariable(EnvironmentSettingNames.ContainerName);
            var linuxContainerEventGenerator = new LinuxContainerEventGenerator(containerName, "TestTenant", "TestStamp");
            linuxContainerEventGenerator.LogFunctionTraceEvent2(LogLevel.Information, string.Empty, string.Empty,
                string.Empty, string.Empty, string.Empty, string.Empty, message, string.Empty, string.Empty,
                string.Empty, string.Empty, string.Empty);
        }

        public void Configure(ScriptApplicationHostOptions options)
        {
            Log("XX Configure1");
            Configure(null, options);
        }

        public void Configure(string name, ScriptApplicationHostOptions options)
        {
            Log("XX Configure2");

            _configuration.GetSection(ConfigurationSectionNames.WebHost)
                ?.Bind(options);

            // Indicate that a WebHost is hosting the ScriptHost
            options.HasParentScope = true;

            // During assignment, we need a way to get the non-placeholder ScriptPath
            // while we are still in PlaceholderMode. This is a way for us to request it from the
            // OptionsFactory and still allow other setups to run.
            var currentValueInStandbyMode = _standbyOptions.CurrentValue.InStandbyMode &&
                                            !string.Equals(name, SkipPlaceholder, StringComparison.Ordinal);
            Log("XX Configure2 currentValueInStandbyMode " + currentValueInStandbyMode);

            Log("XX Configure2 current script path " + options.ScriptPath);

            if (currentValueInStandbyMode)
            {
                Log("XX Setting temp paths ");

                // If we're in standby mode, override relevant properties with values
                // to be used by the placeholder site.
                // Important that we use paths that are different than the configured paths
                // to ensure that placeholder files are isolated
                string tempRoot = Path.GetTempPath();

                options.LogPath = Path.Combine(tempRoot, @"functions\standby\logs");
                options.ScriptPath = Path.Combine(tempRoot, @"functions\standby\wwwroot");
                options.SecretsPath = Path.Combine(tempRoot, @"functions\standby\secrets");
                options.IsSelfHost = options.IsSelfHost;
            }
            else
            {
                Log("XX NOT Setting temp paths ");
            }
        }

        public void Dispose()
        {
            _standbyOptionsOnChangeSubscription?.Dispose();
        }
    }
}
