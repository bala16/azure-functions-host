// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Metering
{
    public class FunctionExecutionActivity
    {
        public FunctionExecutionActivity(string functionName, ExecutionStage executionStage)
        {
            FunctionName = functionName;
            ExecutionStage = executionStage;
        }

        public string FunctionName { get; }

        public ExecutionStage ExecutionStage { get; }
    }
}
