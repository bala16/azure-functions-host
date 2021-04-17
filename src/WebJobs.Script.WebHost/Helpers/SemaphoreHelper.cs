// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Helpers
{
    public class SemaphoreHelper
    {
        public virtual bool Wait(SemaphoreSlim semaphore, int millisecondsTimeout)
        {
            return semaphore.Wait(millisecondsTimeout);
        }

        public virtual int Release(SemaphoreSlim semaphore)
        {
            return semaphore.Release();
        }
    }
}