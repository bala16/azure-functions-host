// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Helpers
{
    public interface IScheduledDisposer
    {
        bool ScheduleDispose(IDisposable disposable);
    }
}