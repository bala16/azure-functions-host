// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using FunctionMetadata = Microsoft.Azure.WebJobs.Script.Description.FunctionMetadata;

namespace Microsoft.Azure.WebJobs.Script.Extensions
{
    public class FunctionMetadataNameComparer : IComparer<FunctionMetadata>
    {
        public static readonly IComparer<FunctionMetadata> Instance = new FunctionMetadataNameComparer();

        public int Compare(FunctionMetadata x, FunctionMetadata y)
        {
            if (x == null || y == null || x.Name == null || y.Name == null)
            {
                return 0;
            }

            return string.Compare(x.Name, y.Name, StringComparison.OrdinalIgnoreCase);
        }
    }
}
