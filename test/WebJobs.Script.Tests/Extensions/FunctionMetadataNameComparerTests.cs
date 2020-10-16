// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Extensions;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Extensions
{
    public class FunctionMetadataNameComparerTests
    {
        [Fact]
        public void Sorts_FunctionsMetadata_By_FunctionName()
        {
            var unsorted = new List<FunctionMetadata>();

            var one = new FunctionMetadata() { Name = "1" };
            var two = new FunctionMetadata() { Name = "2" };
            var three = new FunctionMetadata() { Name = "3" };
            var four = new FunctionMetadata() { Name = "4" };

            unsorted.Add(two);
            unsorted.Add(three);
            unsorted.Add(one);
            unsorted.Add(four);

            unsorted.Sort(FunctionMetadataNameComparer.Instance);

            Assert.Equal(one, unsorted[0]);
            Assert.Equal(two, unsorted[1]);
            Assert.Equal(three, unsorted[2]);
            Assert.Equal(four, unsorted[3]);
        }
    }
}
