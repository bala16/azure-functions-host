﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public interface IEncyptedHostAssignmentContextReader
    {
        Task<string> Read(string uri, CancellationToken cancellationToken);
    }
}
