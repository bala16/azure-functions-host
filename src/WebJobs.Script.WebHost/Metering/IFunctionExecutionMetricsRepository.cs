// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Metering
{
    public interface IFunctionExecutionMetricsRepository
    {
        // this should take FunctionExecutionMetrics instead
        Task WriteFunctionExecutionMetrics(IEnumerable<TrackedFunctionExecutionActivity> activities);
    }
}
