// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Models
{
    public class ContainerFunctionExecutionActivity
    {
        public DateTime EventTime { get; set; }

        public string FunctionName { get; set; }

        public ExecutionStage ExecutionStage { get; set; }

        public string TriggerType { get; set; }

        public bool Success { get; set; }
    }
}
