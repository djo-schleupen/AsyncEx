﻿using System;
using System.Threading.Tasks;
using Nito.AsyncEx;
using System.Linq;
using System.Threading;
using System.Diagnostics.CodeAnalysis;
using Xunit;
using Nito.AsyncEx.Testing;

namespace UnitTests
{
    public class AsyncSemaphoreUnitTests
    {
        [Fact]
        public void Ctor_WhereInitialCountIsLessThanZero_ThrowsArgumentOutOfRangeException()
        {
            var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new AsyncSemaphore(-1));
            Assert.Equal("initialCount", exception.ParamName);
        }

        [Fact]
        public void Ctor_WhereMaximumCountIsLessThanZero_ThrowsArgumentOutOfRangeException()
        {
            var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new AsyncSemaphore(-1, -1, null));
            Assert.Equal("maximumCount", exception.ParamName);
        }

        [Fact]
        public void Ctor_WhereInitialCountIsLessThanZeroAndQueueIsNull_ThrowsArgumentOutOfRangeException()
        {
            var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new AsyncSemaphore(-1, 0, null));
            Assert.Equal("initialCount", exception.ParamName);
        }

        [Fact]
        public void Ctor_WhereInitialCountIsGreaterThanMaximumCount_ThrowsArgumentException()
        {
            var exception = Assert.Throws<ArgumentException>(() => new AsyncSemaphore(1, 0, null));
            Assert.Equal("initialCount", exception.ParamName);
        }

        [Fact]
        public async Task WaitAsync_NoSlotsAvailable_IsNotCompleted()
        {
            var semaphore = new AsyncSemaphore(0);
            Assert.Equal(0, semaphore.CurrentCount);
            var task = semaphore.WaitAsync();
            Assert.Equal(0, semaphore.CurrentCount);
            await AsyncAssert.NeverCompletesAsync(task);
        }

        [Fact]
        public async Task WaitAsync_SlotAvailable_IsCompleted()
        {
            var semaphore = new AsyncSemaphore(1);
            Assert.Equal(1, semaphore.CurrentCount);
            var task1 = semaphore.WaitAsync();
            Assert.Equal(0, semaphore.CurrentCount);
            Assert.True(task1.IsCompleted);
            var task2 = semaphore.WaitAsync();
            Assert.Equal(0, semaphore.CurrentCount);
            await AsyncAssert.NeverCompletesAsync(task2);
        }

        [Fact]
        public void WaitAsync_PreCancelled_SlotAvailable_SucceedsSynchronously()
        {
            var semaphore = new AsyncSemaphore(1);
            Assert.Equal(1, semaphore.CurrentCount);
            var token = new CancellationToken(true);

            var task = semaphore.WaitAsync(token);
            
            Assert.Equal(0, semaphore.CurrentCount);
            Assert.True(task.IsCompleted);
            Assert.False(task.IsCanceled);
            Assert.False(task.IsFaulted);
        }

        [Fact]
        public void WaitAsync_PreCancelled_NoSlotAvailable_CancelsSynchronously()
        {
            var semaphore = new AsyncSemaphore(0);
            Assert.Equal(0, semaphore.CurrentCount);
            var token = new CancellationToken(true);

            var task = semaphore.WaitAsync(token);

            Assert.Equal(0, semaphore.CurrentCount);
            Assert.True(task.IsCompleted);
            Assert.True(task.IsCanceled);
            Assert.False(task.IsFaulted);
        }

        [Fact]
        public async Task WaitAsync_Cancelled_DoesNotTakeSlot()
        {
            var semaphore = new AsyncSemaphore(0);
            Assert.Equal(0, semaphore.CurrentCount);
            var cts = new CancellationTokenSource();
            var task = semaphore.WaitAsync(cts.Token);
            Assert.Equal(0, semaphore.CurrentCount);
            Assert.False(task.IsCompleted);

            cts.Cancel();

            try { await task; }
            catch (OperationCanceledException) { }
            semaphore.Release();
            Assert.Equal(1, semaphore.CurrentCount);
            Assert.True(task.IsCanceled);
        }

        [Fact]
        public void Release_WithoutWaiters_IncrementsCount()
        {
            var semaphore = new AsyncSemaphore(0);
            Assert.Equal(0, semaphore.CurrentCount);
            semaphore.Release();
            Assert.Equal(1, semaphore.CurrentCount);
            var task = semaphore.WaitAsync();
            Assert.Equal(0, semaphore.CurrentCount);
            Assert.True(task.IsCompleted);
        }

        [Fact]
        public async Task Release_WithWaiters_ReleasesWaiters()
        {
            var semaphore = new AsyncSemaphore(0);
            Assert.Equal(0, semaphore.CurrentCount);
            var task = semaphore.WaitAsync();
            Assert.Equal(0, semaphore.CurrentCount);
            Assert.False(task.IsCompleted);
            semaphore.Release();
            Assert.Equal(0, semaphore.CurrentCount);
            await task;
        }

        [Fact]
        public void Release_Overflow_ThrowsException()
        {
            var semaphore = new AsyncSemaphore(long.MaxValue);
            Assert.Equal(long.MaxValue, semaphore.CurrentCount);
            AsyncAssert.Throws<OverflowException>(() => semaphore.Release());
        }

        [Fact]
        public void Release_ZeroSlots_HasNoEffect()
        {
            var semaphore = new AsyncSemaphore(1);
            Assert.Equal(1, semaphore.CurrentCount);
            semaphore.Release(0);
            Assert.Equal(1, semaphore.CurrentCount);
        }

        [Fact]
        public void Set_MaximumCount_ToLessThanZero_ThrowsArgumentOutOfRangeException()
        {
            var semaphore = new AsyncSemaphore(1);
            Assert.Equal(1, semaphore.CurrentCount);
            Assert.Throws<ArgumentOutOfRangeException>(() => semaphore.MaximumCount = -1);
        }

        [Fact]
        public void Set_MaximumCount_ToLowerValue_DecrementsCount()
        {
            var semaphore = new AsyncSemaphore(1);
            Assert.Equal((1, 1), semaphore.AllCount);
            semaphore.MaximumCount = 0;
            Assert.Equal((0, 0), semaphore.AllCount);
        }

        [Fact]
        public void Set_MaximumCount_ToHigherValue_WithoutWaiters_IncrementsCount()
        {
            var semaphore = new AsyncSemaphore(1);
            Assert.Equal((1, 1), semaphore.AllCount);
            semaphore.MaximumCount = 2;
            Assert.Equal((2, 2), semaphore.AllCount);
        }

        [Fact]
        public async Task Set_MaximumCount_ToHigherValue_WithWaiters_ReleasesWaiters()
        {
            var semaphore = new AsyncSemaphore(0);
            Assert.Equal(0, semaphore.CurrentCount);
            var task = semaphore.WaitAsync();
            Assert.Equal(0, semaphore.CurrentCount);
            Assert.False(task.IsCompleted);
            semaphore.MaximumCount = 1;
            Assert.Equal(0, semaphore.CurrentCount);
            await task;
        }

        [Fact]
        public void Id_IsNotZero()
        {
            var semaphore = new AsyncSemaphore(0);
            Assert.NotEqual(0, semaphore.Id);
        }
    }
}
