using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Nito.Collections;

[assembly:InternalsVisibleTo("AsyncEx.Coordination.UnitTests, PublicKey=0024000004800000140400000602000000240000525341310020000001000100e13b42a43cad721708ce088bb578a6821a739bd5f1eb3a94b036806f7c839dc7c5b4c2e016d741ef11d1834e9c03f2691b20d3fa7880f09ad811128e116db5be52db90001dad10b1e4646056e64b37ebcd2477107620d348e1d892359ce4b38218b62d2631075b4b1f449131a614ab4c6e42d36d96d57faa56e21e8b25b1b909a39b62fbc8568343d604f0994619e32429268a97752b97303c2c6c51ea64a98bd4063f570d08c3673ed243e22a64dffd948fe198d1f0e9d0b6b191c819878595e69047803d02395b1f4bf4520c6dac227a653505d3c90f5644d2591ab9cef82ad27f63e606d3fb3c7624745df3265e1c097045546ded7f6a19d21cf7e31638fe54ae71f2d66bc3c294c1ca8ed6c944e955ebfcb056f2686ac4a4475e07d0535753ef2a145700175b2af0d2d24136c34265ef2a29170f405bf2547b848fdcb15dbcb94fe4f3bdf5bc7c0cbaa1e1952e429bae89fd745650a69567f7b7a873d682fcd43f993ccc02a296b227b1f108e5d5b3c699216f70fe6b732a8bfbc8b270cb3c8bb3386f47983c406fbb8d761f005b9cee1556e34f06ac983501e7fd9a6115394daa055f5130aa485fcc2ab60ee937f10424699cc5ce8dba1187d300d8934191988bf5453ec00942abcf1449085f7a7df10db6a3b1d50e5db74fb188b08dcc0a77ea1e75ca46ee37562de8272da44bf071c8714c5e29c89c6159dd2fa6f59153440ad558301cc760f4a0aaf35bd7337046d34520997370102c53d77c148c8e0106bc892904dca00a864673d8a699289c5ce7378dbee9cdd54d7a4cbe28d6fb4ed4a18f24589432c6850835cd8546a13f885d8aad2b2ebb9d1f5564abc5c2e6c79bddc0520f5188889b66767c064c1aee18b3f2b33dcc6eb774e5d67cd6de27cb3c104795c520e4b022b4d393e8021c33c9abb395475e16c51e3a9e3a7c9abe4625ae1732233e6b79a373056b17da330d6277e9305b3ed07a39743ef6363cecb47bc4c2d67a14f701796ec2751ef8793582c51e2c623f050784b40c31d68c94872e99bd0ddc8aac4e7863cb4687b0a68d146fb175b59aa6241bc5cd97b9a80b71f26b3d7ee35068135999221cbee1f00e66457d0fda2e7611c642c750a73a5edaad97b6788e03c30302112799b750288ac76af928ca856472e5b26a17793bea06bb9156b6ccdf423b42deb19bd086cf00b98f906ed537aaa721321c7098d794610310d63f86ca39218dd964f95b5d337728b1117b8d5d8dadf1294b5e6199ab93c0fb38754815b16e60a3cb9e6b50d1a33ed261b74a0ef71a1f155cd9d9e16e747871a018482c070e434b385060ed08088493cddf055d4af04753cce9a54f83f85010c25a748ac876d6be22138fb228ceaf57902ee4e3b94b488f9e3534a8dc438cc9bac5e9035334aa5250bcfbbfa279e542d10e9096134090d5613df9c6d5")]

namespace Nito.AsyncEx
{
    /// <summary>
    /// A collection of cancelable <see cref="TaskCompletionSource{T}"/> instances. Implementations must assume the caller is holding a lock.
    /// </summary>
    /// <typeparam name="T">The type of the results. If this isn't needed, use <see cref="Object"/>.</typeparam>
    internal interface IAsyncWaitQueue<T>
    {
        /// <summary>
        /// Gets whether the queue is empty.
        /// </summary>
        bool IsEmpty { get; }

        /// <summary>
        /// Creates a new entry and queues it to this wait queue. The returned task must support both synchronous and asynchronous waits.
        /// </summary>
        /// <returns>The queued task.</returns>
        Task<T> Enqueue();

        /// <summary>
        /// Removes a single entry in the wait queue and completes it. This method may only be called if <see cref="IsEmpty"/> is <c>false</c>. The task continuations for the completed task must be executed asynchronously.
        /// </summary>
        /// <param name="result">The result used to complete the wait queue entry. If this isn't needed, use <c>default(T)</c>.</param>
        void Dequeue(T? result = default);

        /// <summary>
        /// Removes all entries in the wait queue and completes them. The task continuations for the completed tasks must be executed asynchronously.
        /// </summary>
        /// <param name="result">The result used to complete the wait queue entries. If this isn't needed, use <c>default(T)</c>.</param>
        void DequeueAll(T? result = default);

        /// <summary>
        /// Attempts to remove an entry from the wait queue and cancels it. The task continuations for the completed task must be executed asynchronously.
        /// </summary>
        /// <param name="task">The task to cancel.</param>
        /// <param name="cancellationToken">The cancellation token to use to cancel the task.</param>
        bool TryCancel(Task task, CancellationToken cancellationToken);

        /// <summary>
        /// Removes all entries from the wait queue and cancels them. The task continuations for the completed tasks must be executed asynchronously.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token to use to cancel the tasks.</param>
        void CancelAll(CancellationToken cancellationToken);
    }

    /// <summary>
    /// Provides extension methods for wait queues.
    /// </summary>
    internal static class AsyncWaitQueueExtensions
    {
        /// <summary>
        /// Creates a new entry and queues it to this wait queue. If the cancellation token is already canceled, this method immediately returns a canceled task without modifying the wait queue.
        /// </summary>
        /// <param name="this">The wait queue.</param>
        /// <param name="mutex">A synchronization object taken while cancelling the entry.</param>
        /// <param name="token">The token used to cancel the wait.</param>
        /// <returns>The queued task.</returns>
        public static Task<T> Enqueue<T>(this IAsyncWaitQueue<T> @this, object mutex, CancellationToken token)
        {
            if (token.IsCancellationRequested)
                return Task.FromCanceled<T>(token);

            var ret = @this.Enqueue();
            if (!token.CanBeCanceled)
                return ret;

            var registration = token.Register(() =>
            {
                lock (mutex)
                    @this.TryCancel(ret, token);
            }, useSynchronizationContext: false);
            ret.ContinueWith(_ => registration.Dispose(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return ret;
        }
    }

    /// <summary>
    /// The default wait queue implementation, which uses a double-ended queue.
    /// </summary>
    /// <typeparam name="T">The type of the results. If this isn't needed, use <see cref="Object"/>.</typeparam>
    [DebuggerDisplay("Count = {Count}")]
    [DebuggerTypeProxy(typeof(DefaultAsyncWaitQueue<>.DebugView))]
    internal sealed class DefaultAsyncWaitQueue<T> : IAsyncWaitQueue<T>
    {
        private readonly Deque<TaskCompletionSource<T>> _queue = new Deque<TaskCompletionSource<T>>();

        private int Count
        {
            get { return _queue.Count; }
        }

        bool IAsyncWaitQueue<T>.IsEmpty
        {
            get { return Count == 0; }
        }

        Task<T> IAsyncWaitQueue<T>.Enqueue()
        {
            var tcs = TaskCompletionSourceExtensions.CreateAsyncTaskSource<T>();
            _queue.AddToBack(tcs);
            return tcs.Task;
        }

        void IAsyncWaitQueue<T>.Dequeue(T? result)
        {
            _queue.RemoveFromFront().TrySetResult(result!);
        }

        void IAsyncWaitQueue<T>.DequeueAll(T? result)
        {
            foreach (var source in _queue)
                source.TrySetResult(result!);
            _queue.Clear();
        }

        bool IAsyncWaitQueue<T>.TryCancel(Task task, CancellationToken cancellationToken)
        {
            for (int i = 0; i != _queue.Count; ++i)
            {
                if (_queue[i].Task == task)
                {
                    _queue[i].TrySetCanceled(cancellationToken);
                    _queue.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }

        void IAsyncWaitQueue<T>.CancelAll(CancellationToken cancellationToken)
        {
            foreach (var source in _queue)
                source.TrySetCanceled(cancellationToken);
            _queue.Clear();
        }

        [DebuggerNonUserCode]
        internal sealed class DebugView
        {
            private readonly DefaultAsyncWaitQueue<T> _queue;

            public DebugView(DefaultAsyncWaitQueue<T> queue)
            {
                _queue = queue;
            }

            [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
            public Task<T>[] Tasks
            {
                get
                {
                    var result = new List<Task<T>>(_queue._queue.Count);
                    foreach (var entry in _queue._queue)
                        result.Add(entry.Task);
                    return result.ToArray();
                }
            }
        }
    }
}
