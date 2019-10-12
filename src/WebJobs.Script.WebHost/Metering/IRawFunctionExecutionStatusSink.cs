// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Metering
{
    public interface IRawFunctionExecutionStatusSink
    {
        void TryAddFunctionActivityWithTime(TrackedFunctionExecutionActivity activity);

        IReadOnlyCollection<TrackedFunctionExecutionActivity> GetActivities();
    }
}