// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Host
{
    // Returns true if ScriptHost was created in Standby mode
    public class ScriptHostStandbyStateProvider
    {
        public ScriptHostStandbyStateProvider(bool isStandbyScriptHost)
        {
            IsStandbyScriptHost = isStandbyScriptHost;
        }

        public bool IsStandbyScriptHost { get; } = false;
    }
}