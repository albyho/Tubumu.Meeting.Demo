using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Tubumu.Core.Extensions
{
    public static class TaskExtensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)] // 造成编译器优化调用
        public static void NoWarning(this Task _)
        {
        }

        /// <summary>
        /// Returns a task that completes as the original task completes or when a timeout expires,
        /// whichever happens first.
        /// </summary>
        /// <param name="task">The task to wait for.</param>
        /// <param name="timeout">The maximum time to wait.</param>
        /// <returns>
        /// A task that completes with the result of the specified <paramref name="task"/> or
        /// faults with a <see cref="TimeoutException"/> if <paramref name="timeout"/> elapses first.
        /// </returns>
        public static async Task WithTimeout(this Task task, TimeSpan timeout, Action cancelled = null)
        {
            using (var timerCancellation = new CancellationTokenSource())
            {
                Task timeoutTask = Task.Delay(timeout, timerCancellation.Token);
                Task firstCompletedTask = await Task.WhenAny(task, timeoutTask).ConfigureAwait(false);
                if (firstCompletedTask == timeoutTask)
                {
                    if (cancelled == null)
                    {
                        throw new TimeoutException();
                    }
                    cancelled();
                }

                // The timeout did not elapse, so cancel the timer to recover system resources.
                timerCancellation.Cancel();

                // re-throw any exceptions from the completed task.
                await task.ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Returns a task that completes as the original task completes or when a timeout expires,
        /// whichever happens first.
        /// </summary>
        /// <typeparam name="T">The type of value returned by the original task.</typeparam>
        /// <param name="task">The task to wait for.</param>
        /// <param name="timeout">The maximum time to wait.</param>
        /// <returns>
        /// A task that completes with the result of the specified <paramref name="task"/> or
        /// faults with a <see cref="TimeoutException"/> if <paramref name="timeout"/> elapses first.
        /// </returns>
        public static async Task<T> WithTimeout<T>(this Task<T> task, TimeSpan timeout, Action cancelled = null)
        {
            await WithTimeout((Task)task, timeout, cancelled).ConfigureAwait(false);
            return task.GetAwaiter().GetResult();
        }

        /// <summary>
        /// Creates a new Task that mirrors the supplied task but that will be canceled after the specified timeout.
        /// </summary>
        /// <typeparam name="TResult">Specifies the type of data contained in the task.</typeparam>
        /// <param name="task">The task.</param>
        /// <param name="timeout">The timeout.</param>
        /// <returns>The new Task that may time out.</returns>
        public static Task<TResult> WithTimeout<TResult>(this Task<TResult> task, TimeSpan timeout)
        {
            var result = new TaskCompletionSource<TResult>(task.AsyncState);
            var timer = new Timer(state => ((TaskCompletionSource<TResult>)state).TrySetCanceled(), result, timeout, TimeSpan.FromMilliseconds(-1));
            task.ContinueWith(t =>
            {
                timer.Dispose();
                result.TrySetFromTask(t);
            }, TaskContinuationOptions.ExecuteSynchronously);
            return result.Task;
        }

        /// <summary>
        /// Attempts to transfer the result of a Task to the TaskCompletionSource.
        /// </summary>
        /// <typeparam name="TResult">Specifies the type of the result.</typeparam>
        /// <param name="resultSetter">The TaskCompletionSource.</param>
        /// <param name="task">The task whose completion results should be transfered.</param>
        /// <returns>Whether the transfer could be completed.</returns>
        public static bool TrySetFromTask<TResult>(this TaskCompletionSource<TResult> resultSetter, Task task)
        {
            switch (task.Status)
            {
                case TaskStatus.RanToCompletion:
                    return resultSetter.TrySetResult(task is Task<TResult> taskLocal ? taskLocal.Result : default);

                case TaskStatus.Faulted:
                    return resultSetter.TrySetException(task.Exception.InnerExceptions);

                case TaskStatus.Canceled:
                    return resultSetter.TrySetCanceled();

                default:
                    throw new InvalidOperationException("The task was not completed.");
            }
        }

        /// <summary>
        /// Attempts to transfer the result of a Task to the TaskCompletionSource.
        /// </summary>
        /// <typeparam name="TResult">Specifies the type of the result.</typeparam>
        /// <param name="resultSetter">The TaskCompletionSource.</param>
        /// <param name="task">The task whose completion results should be transfered.</param>
        /// <returns>Whether the transfer could be completed.</returns>
        public static bool TrySetFromTask<TResult>(this TaskCompletionSource<TResult> resultSetter, Task<TResult> task)
        {
            return TrySetFromTask(resultSetter, (Task)task);
        }

        public static Task<T> FromResult<T>(T value)
        {
            var tcs = new TaskCompletionSource<T>();
            tcs.SetResult(value);
            return tcs.Task;
        }

        public static TaskCompletionSource<TResult> WithTimeout<TResult>(this TaskCompletionSource<TResult> taskCompletionSource, TimeSpan timeout)
        {
            return WithTimeout(taskCompletionSource, timeout, null);
        }

        public static TaskCompletionSource<TResult> WithTimeout<TResult>(this TaskCompletionSource<TResult> taskCompletionSource, TimeSpan timeout, Action cancelled)
        {
            Timer timer = null;
            timer = new Timer(state =>
            {
                timer.Dispose();
                if (taskCompletionSource.Task.Status != TaskStatus.RanToCompletion)
                {
                    taskCompletionSource.TrySetCanceled();
                    cancelled?.Invoke();
                }
            }, null, timeout, TimeSpan.FromMilliseconds(-1));

            return taskCompletionSource;
        }

        /// <summary>
        /// Set the TaskCompletionSource in an async fashion. This prevents the Task Continuation being executed sync on the same thread
        /// This is required otherwise contintinuations will happen on CEF UI threads
        /// </summary>
        /// <typeparam name="TResult">Generic param</typeparam>
        /// <param name="taskCompletionSource">tcs</param>
        /// <param name="result">result</param>
        public static void TrySetResultAsync<TResult>(this TaskCompletionSource<TResult> taskCompletionSource, TResult result)
        {
            Task.Factory.StartNew(() => taskCompletionSource.TrySetResult(result), CancellationToken.None, TaskCreationOptions.None, TaskScheduler.Default);
        }
    }
}
