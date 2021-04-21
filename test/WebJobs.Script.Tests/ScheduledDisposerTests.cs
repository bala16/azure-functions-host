// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class ScheduledDisposerTests
    {
        private const int TimeOutMs = 50;

        private readonly Mock<SemaphoreHelper> _semaphoreHelper;

        public ScheduledDisposerTests()
        {
            _semaphoreHelper = new Mock<SemaphoreHelper>(MockBehavior.Strict);
            _semaphoreHelper.Setup(l => l.Wait(It.IsAny<SemaphoreSlim>(), It.IsAny<int>())).Returns(true);
            _semaphoreHelper.Setup(l => l.Release(It.IsAny<SemaphoreSlim>())).Returns(1);
        }

        [Fact]
        public async Task SchedulesDispose()
        {
            using (var scheduledDisposer = new ScheduledDisposer(NullLogger<ScheduledDisposer>.Instance, _semaphoreHelper.Object, TimeOutMs))
            {
                var disposable = new Mock<IDisposable>();
                disposable.Setup(d => d.Dispose());
                Assert.True(scheduledDisposer.ScheduleDispose(disposable.Object));
                await Task.Delay(TimeSpan.FromMilliseconds(2 * TimeOutMs));
                _semaphoreHelper.Verify(l => l.Wait(It.IsAny<SemaphoreSlim>(), It.IsAny<int>()), Times.Exactly(2));
                _semaphoreHelper.Verify(l => l.Release(It.IsAny<SemaphoreSlim>()), Times.Exactly(2));

                disposable.Verify(d => d.Dispose(), Times.Exactly(1));
            }
        }

        [Fact]
        public async Task DoesNotScheduleDisposeIfLockNotAcquired()
        {
            var semaphoreHelper = new Mock<SemaphoreHelper>(MockBehavior.Strict);
            semaphoreHelper.Setup(l => l.Wait(It.IsAny<SemaphoreSlim>(), It.IsAny<int>())).Returns(false);

            using (var scheduledDisposer =
                new ScheduledDisposer(NullLogger<ScheduledDisposer>.Instance, semaphoreHelper.Object, TimeOutMs))
            {
                var disposable = new Mock<IDisposable>();
                disposable.Setup(d => d.Dispose());

                Assert.False(scheduledDisposer.ScheduleDispose(disposable.Object));
                await Task.Delay(TimeSpan.FromMilliseconds(2 * TimeOutMs));
                disposable.Verify(d => d.Dispose(), Times.Once);
                semaphoreHelper.Verify(l => l.Release(It.IsAny<SemaphoreSlim>()), Times.Never);
            }
        }

        [Fact]
        public async Task DoesNotScheduleDisposeIfDisposeInProgress()
        {
            using (var scheduledDisposer =
                new ScheduledDisposer(NullLogger<ScheduledDisposer>.Instance, _semaphoreHelper.Object, TimeOutMs))
            {
                var disposable = new Mock<IDisposable>();
                Assert.True(scheduledDisposer.ScheduleDispose(disposable.Object)); // First dispose
                Assert.False(scheduledDisposer.ScheduleDispose(disposable.Object)); // 2nd dispose will complete on current thread
                await Task.Delay(TimeSpan.FromMilliseconds(2 * TimeOutMs));

                _semaphoreHelper.Verify(l => l.Wait(It.IsAny<SemaphoreSlim>(), It.IsAny<int>()), Times.Exactly(3));
                _semaphoreHelper.Verify(l => l.Release(It.IsAny<SemaphoreSlim>()), Times.Exactly(3));
                disposable.Verify(d => d.Dispose(), Times.Exactly(2));
            }
        }

        [Fact]
        public async Task DisposesOnCurrentThreadOnException()
        {
            var semaphoreHelper = new Mock<SemaphoreHelper>(MockBehavior.Strict);
            semaphoreHelper.Setup(l => l.Wait(It.IsAny<SemaphoreSlim>(), It.IsAny<int>())).Throws<Exception>();

            using (var scheduledDisposer =
                new ScheduledDisposer(NullLogger<ScheduledDisposer>.Instance, semaphoreHelper.Object, TimeOutMs))
            {
                var disposable = new Mock<IDisposable>();
                Assert.False(scheduledDisposer.ScheduleDispose(disposable.Object));
                await Task.Delay(TimeSpan.FromMilliseconds(2 * TimeOutMs));

                semaphoreHelper.Verify(l => l.Wait(It.IsAny<SemaphoreSlim>(), It.IsAny<int>()), Times.Exactly(1));
                semaphoreHelper.Verify(l => l.Release(It.IsAny<SemaphoreSlim>()), Times.Never);
                disposable.Verify(d => d.Dispose(), Times.Exactly(1));
            }
        }
    }
}
