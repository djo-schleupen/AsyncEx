﻿using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Nito.AsyncEx.Synchronous;

// Original idea from Stephen Toub: http://blogs.msdn.com/b/pfxteam/archive/2012/02/12/10266983.aspx

namespace Nito.AsyncEx
{
    /// <summary>
    /// An async-compatible semaphore. Alternatively, you could use <c>SemaphoreSlim</c>.
    /// </summary>
    [DebuggerDisplay("Id = {Id}, CurrentCount = {_count}, MaximumCount = {_maximumCount}, WaitQueue.IsEmpty = {_queue.IsEmpty}")]
    [DebuggerTypeProxy(typeof(DebugView))]
    public sealed class AsyncSemaphore
    {
        /// <summary>
        /// The queue of TCSs that other tasks are awaiting to acquire the semaphore.
        /// </summary>
        private readonly IAsyncWaitQueue<object> _queue;

        /// <summary>
        /// The number of waits that will be immediately granted.
        /// </summary>
        private long _count;

        /// <summary>
        /// The maximum number of waits that may be immediately granted at any time.
        /// </summary>
        private long _maximumCount;

        /// <summary>
        /// The semi-unique identifier for this instance. This is 0 if the id has not yet been created.
        /// </summary>
        private int _id;

        /// <summary>
        /// The object used for mutual exclusion.
        /// </summary>
        private readonly object _mutex;

        /// <summary>
        /// Creates a new async-compatible semaphore with the specified initial count.
        /// </summary>
        /// <param name="initialCount">The initial count for this semaphore.</param>
        /// <param name="maximumCount">The maximum count for this semaphore. This must be greater than or equal to zero.</param>
        /// <param name="queue">The wait queue used to manage waiters. This may be <c>null</c> to use a default (FIFO) queue.</param>
        internal AsyncSemaphore(long initialCount, long maximumCount, IAsyncWaitQueue<object>? queue)
        {
            if (maximumCount < 0)
                throw new ArgumentOutOfRangeException(nameof(maximumCount), "Must be greater than or equal to 0.");
            if (initialCount > maximumCount)
                throw new ArgumentException($"Must be less than or equal to {nameof(maximumCount)}.", nameof(initialCount));
            if (initialCount < 0 && queue == null)
                throw new ArgumentOutOfRangeException(nameof(initialCount), $"Must be greater than or equal to 0, unless {nameof(queue)} is non-null.");

            _queue = queue ?? new DefaultAsyncWaitQueue<object>();
            _count = initialCount;
            _maximumCount = maximumCount;
            _mutex = new object();
        }

        /// <summary>
        /// Creates a new async-compatible semaphore with the specified initial count.
        /// </summary>
        /// <param name="initialCount">The initial count for this semaphore.</param>
        /// <param name="queue">The wait queue used to manage waiters. This may be <c>null</c> to use a default (FIFO) queue.</param>
        internal AsyncSemaphore(long initialCount, IAsyncWaitQueue<object>? queue)
            : this(initialCount, Math.Max(initialCount, 0), queue)
        {
        }

        /// <summary>
        /// Creates a new async-compatible semaphore with the specified initial count.
        /// </summary>
        /// <param name="initialCount">The initial count for this semaphore. This must be greater than or equal to zero.</param>
        /// <param name="maximumCount">The maximum count for this semaphore. This must be greater than or equal to <paramref name="initialCount"/>.</param>
        public AsyncSemaphore(long initialCount, long maximumCount)
            : this(initialCount, maximumCount, null)
        {
        }

        /// <summary>
        /// Creates a new async-compatible semaphore with the specified initial count.
        /// </summary>
        /// <param name="initialCount">The initial count for this semaphore. This must be greater than or equal to zero.</param>
        public AsyncSemaphore(long initialCount)
            : this(initialCount, null)
        {
        }

        /// <summary>
        /// Gets a semi-unique identifier for this asynchronous semaphore.
        /// </summary>
        public int Id
        {
            get { return IdManager<AsyncSemaphore>.GetId(ref _id); }
        }

        /// <summary>
        /// Gets the number of slots currently available on this semaphore. This member is seldom used; code using this member has a high possibility of race conditions.
        /// </summary>
        public long CurrentCount
        {
            get { lock (_mutex) { return _count; } }
        }

        /// <summary>
        /// Gets the maximum number of slots potentially available on this semaphore.
        /// </summary>
        public long MaximumCount
        {
            get { lock (_mutex) { return _maximumCount; } }
            set {
                lock (_mutex)
                {
                    if (value < 0)
                        throw new ArgumentOutOfRangeException(nameof(MaximumCount), "Must be greater than or equal to 0.");

                    _count += (value - _maximumCount);
                    _maximumCount = value;

                    while (_count > 0 && !_queue.IsEmpty)
                    {
                        _queue.Dequeue();
                        --_count;
                    }
                }
            }
        }

        /// <summary>
        /// Gets the current and maximum number of slots available on this semaphore.
        /// </summary>
        public (long CurrentCount, long MaximumCount) AllCount
        {
            get { lock (_mutex) { return (_count, _maximumCount); } }
        }

        /// <summary>
        /// Asynchronously waits for a slot in the semaphore to be available.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token used to cancel the wait. If this is already set, then this method will attempt to take the slot immediately (succeeding if a slot is currently available).</param>
        public Task WaitAsync(CancellationToken cancellationToken)
        {
            Task ret;
            lock (_mutex)
            {
                // If the semaphore is available, take it immediately and return.
                if (_count > 0)
                {
                    --_count;
                    ret = TaskConstants.Completed;
                }
                else
                {
                    // Wait for the semaphore to become available or cancellation.
                    ret = _queue.Enqueue(_mutex, cancellationToken);
                }
            }

            return ret;
        }

        /// <summary>
        /// Asynchronously waits for a slot in the semaphore to be available.
        /// </summary>
        public Task WaitAsync()
        {
            return WaitAsync(CancellationToken.None);
        }

        /// <summary>
        /// Synchronously waits for a slot in the semaphore to be available. This method may block the calling thread.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token used to cancel the wait. If this is already set, then this method will attempt to take the slot immediately (succeeding if a slot is currently available).</param>
        public void Wait(CancellationToken cancellationToken)
        {
            WaitAsync(cancellationToken).WaitAndUnwrapException(CancellationToken.None);
        }

        /// <summary>
        /// Synchronously waits for a slot in the semaphore to be available. This method may block the calling thread.
        /// </summary>
        public void Wait()
        {
            Wait(CancellationToken.None);
        }

        /// <summary>
        /// Releases the semaphore.
        /// </summary>
        public void Release(long releaseCount)
        {
            if (releaseCount < 0)
                throw new ArgumentOutOfRangeException(nameof(releaseCount), "Must be greater than 0.");
            if (releaseCount == 0)
                return;

            lock (_mutex)
            {
                checked
                {
                    var test = _count + releaseCount;
                }

                while (releaseCount != 0 && !_queue.IsEmpty)
                {
                    _queue.Dequeue();
                    --releaseCount;
                }
                _count += releaseCount;
            }
        }

        /// <summary>
        /// Releases the semaphore.
        /// </summary>
        public void Release()
        {
            Release(1);
        }

        private async Task<IDisposable> DoLockAsync(CancellationToken cancellationToken)
        {
            await WaitAsync(cancellationToken).ConfigureAwait(false);
            return Disposables.Disposable.Create(Release);
        }

        /// <summary>
        /// Asynchronously waits on the semaphore, and returns a disposable that releases the semaphore when disposed, thus treating this semaphore as a "multi-lock".
        /// </summary>
        /// <param name="cancellationToken">The cancellation token used to cancel the wait. If this is already set, then this method will attempt to take the slot immediately (succeeding if a slot is currently available).</param>
        public AwaitableDisposable<IDisposable> LockAsync(CancellationToken cancellationToken)
        {
#pragma warning disable CA2000 // Dispose objects before losing scope
            return new AwaitableDisposable<IDisposable>(DoLockAsync(cancellationToken));
#pragma warning restore CA2000 // Dispose objects before losing scope
        }

        /// <summary>
        /// Asynchronously waits on the semaphore, and returns a disposable that releases the semaphore when disposed, thus treating this semaphore as a "multi-lock".
        /// </summary>
        public AwaitableDisposable<IDisposable> LockAsync() => LockAsync(CancellationToken.None);

        /// <summary>
        /// Synchronously waits on the semaphore, and returns a disposable that releases the semaphore when disposed, thus treating this semaphore as a "multi-lock".
        /// </summary>
        /// <param name="cancellationToken">The cancellation token used to cancel the wait. If this is already set, then this method will attempt to take the slot immediately (succeeding if a slot is currently available).</param>
        public IDisposable Lock(CancellationToken cancellationToken)
        {
            Wait(cancellationToken);
            return Disposables.Disposable.Create(Release);
        }

        /// <summary>
        /// Synchronously waits on the semaphore, and returns a disposable that releases the semaphore when disposed, thus treating this semaphore as a "multi-lock".
        /// </summary>
        public IDisposable Lock() => Lock(CancellationToken.None);

        // ReSharper disable UnusedMember.Local
        [DebuggerNonUserCode]
        private sealed class DebugView
        {
            private readonly AsyncSemaphore _semaphore;

            public DebugView(AsyncSemaphore semaphore)
            {
                _semaphore = semaphore;
            }

            public int Id { get { return _semaphore.Id; } }

            public long CurrentCount { get { return _semaphore._count; } }

            public long MaximumCount { get { return _semaphore._maximumCount; } }

            public IAsyncWaitQueue<object> WaitQueue { get { return _semaphore._queue; } }
        }
        // ReSharper restore UnusedMember.Local
    }
}
