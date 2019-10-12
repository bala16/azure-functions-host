using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Metering
{
    public class TrackedFunctionExecutionActivityCollection : IEnumerable<TrackedFunctionExecutionActivity>
    {
        private readonly IEnumerable<TrackedFunctionExecutionActivity> _activities;

        public TrackedFunctionExecutionActivityCollection(IEnumerable<TrackedFunctionExecutionActivity> activities)
        {
            _activities = activities;
        }

        public IEnumerator<TrackedFunctionExecutionActivity> GetEnumerator()
        {
            return _activities.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }
    }
}
