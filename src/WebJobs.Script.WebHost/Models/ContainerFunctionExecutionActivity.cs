// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Models
{
    public class ContainerFunctionExecutionActivity
    {
        public DateTime EventTime { get; set; }

        public string FunctionName { get; set; }

        public ExecutionStage ExecutionStage { get; set; }

        public string TriggerType { get; set; }

        public bool Success { get; set; }

        public override string ToString()
        {
            return $"{FunctionName}:{ExecutionStage}-{Success}";
        }
    }
}
