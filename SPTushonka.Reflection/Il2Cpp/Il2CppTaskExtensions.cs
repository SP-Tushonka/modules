using System;
using System.Collections;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Diz.Jobs;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Runtime;
using Il2CppTask = Il2CppSystem.Threading.Tasks.Task;

namespace SPTushonka.Reflection.Il2Cpp;

/// <summary>
/// Adapts IL2CPP tasks, managed tasks, and coroutines for use across the runtime boundary.
/// </summary>
/// <remarks>
/// Task adapters preserve success, failure, and cancellation. Exception text crosses the boundary,
/// but the original exception type does not. Awaiting a managed task follows the caller's captured context.
/// </remarks>
public static class Il2CppTaskExtensions
{
    /// <summary>
    /// Adapts an IL2CPP task for managed await. Call from a thread attached to IL2CPP.
    /// </summary>
    public static Task AsManaged(this Il2CppTask task)
    {
        return AsManaged(task, () => true);
    }

    /// <summary>
    /// Adapts an IL2CPP task and reads its result in the IL2CPP completion callback, before resuming managed code.
    /// Call from a thread attached to IL2CPP. Returned game objects still retain their thread requirements.
    /// </summary>
    public static Task<T> AsManaged<T>(this Il2CppSystem.Threading.Tasks.Task<T> task)
    {
        return AsManaged(task, () => task.Result);
    }

    private static Task<T> AsManaged<T>(Il2CppTask task, Func<T> getResult)
    {
        ArgumentNullException.ThrowIfNull(task);
        TaskCompletionSource<T> source = new(TaskCreationOptions.RunContinuationsAsynchronously);
        void Complete(Il2CppTask completed)
        {
            try
            {
                if (completed.IsFaulted)
                {
                    source.TrySetException(new Exception(completed.Exception?.ToString()));
                }
                else if (completed.IsCanceled)
                {
                    source.TrySetCanceled();
                }
                else
                {
                    source.TrySetResult(getResult());
                }
            }
            catch (Exception ex)
            {
                source.TrySetException(ex);
            }
        }

        task.ContinueWith(Complete);
        return source.Task;
    }

    /// <summary>
    /// Creates an IL2CPP task from a managed task. Call on the installed Unity main thread.
    /// Completion is dispatched there. Dispatcher shutdown cancels the adapter without canceling the original task.
    /// </summary>
    public static Il2CppTask ToIl2Cpp(this Task task)
    {
        ArgumentNullException.ThrowIfNull(task);
        MainThread.VerifyAccess();
        Il2CppSystem.Threading.Tasks.TaskCompletionSource<Il2CppSystem.Object> source = new();
        Complete(task, () => source.SetResult(null), exception => source.SetException(exception), () => source.SetCanceled());
        return source.Task;
    }

    /// <summary>
    /// Creates an IL2CPP task carrying the managed result. Call on the installed Unity main thread.
    /// Dispatcher shutdown cancels the adapter without canceling the original task.
    /// </summary>
    public static Il2CppSystem.Threading.Tasks.Task<T> ToIl2Cpp<T>(this Task<T> task)
    {
        ArgumentNullException.ThrowIfNull(task);
        MainThread.VerifyAccess();
        Il2CppSystem.Threading.Tasks.TaskCompletionSource<T> source = new();
        Complete(task, () => source.SetResult(task.Result), exception => source.SetException(exception), () => source.SetCanceled());
        return source.Task;
    }

    private static void Complete(Task task, Action succeeded, Action<Il2CppSystem.Exception> failed, Action canceled)
    {
        // Registration happens on the main thread, as do Finish and dispatcher shutdown.
        // This keeps native task access off worker threads, including during shutdown races.
        var shutdownToken = MainThread.ShutdownToken;
        var registration = shutdownToken.Register(canceled);
        task.ContinueWith(completed =>
        {
            if (shutdownToken.IsCancellationRequested)
            {
                return;
            }

            void Finish()
            {
                try
                {
                    if (completed.IsCanceled)
                    {
                        canceled();
                    }
                    else if (completed.IsFaulted)
                    {
                        failed(new Il2CppSystem.Exception(completed.Exception.ToString()));
                    }
                    else
                    {
                        succeeded();
                    }
                }
                finally
                {
                    registration.Dispose();
                }
            }

            if (MainThread.IsCurrent)
            {
                Finish();
            }
            else
            {
                // If shutdown won the race, the token registration completes the adapter instead.
                MainThread.TryPost(Finish);
            }
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    /// <summary>
    /// Wraps a managed iterator as an IL2CPP IEnumerator, for overrides that must return one. StartCoroutine takes a
    /// managed iterator directly. IL2CPP yield instructions, primitives and strings pass through and nested managed
    /// iterators are wrapped.
    /// </summary>
    public static Il2CppSystem.Collections.IEnumerator ToIl2Cpp(this IEnumerator enumerator)
    {
        return Il2CppObjectPool.Get<Il2CppSystem.Collections.IEnumerator>(ManagedArguments.ToIl2CppEnumerator(enumerator));
    }

    /// <summary>
    /// Exposes a Diz job's IL2CPP awaiter through managed INotifyCompletion.
    /// Use <c>await JobScheduler.Yield().AsManaged()</c>. Diz controls where the continuation runs.
    /// </summary>
    public static JobAwaitable AsManaged(this IJobAwaitable awaitable)
    {
        return new JobAwaitable(awaitable.GetAwaiter());
    }
}

/// <summary>
/// Forwards completion and result retrieval to the game's job awaiter.
/// </summary>
public readonly struct JobAwaitable(IJobAwaiter awaiter) : INotifyCompletion
{
    public bool IsCompleted
    {
        get
        {
            return awaiter.IsCompleted;
        }
    }

    public JobAwaitable GetAwaiter()
    {
        return this;
    }

    public void OnCompleted(Action continuation)
    {
        awaiter.OnCompleted(continuation);
    }

    public void GetResult()
    {
        awaiter.GetResult();
    }
}

