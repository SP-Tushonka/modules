using System;
using System.Collections;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Diz.Jobs;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Injection;
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

        task.ContinueWith(DelegateSupport.ConvertDelegate<Il2CppSystem.Action<Il2CppTask>>(new Action<Il2CppTask>(Complete)));
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
    /// Wraps a managed iterator for Unity. IL2CPP yield instructions pass through and nested managed iterators
    /// are wrapped. Null and unsupported yielded values become a frame delay.
    /// </summary>
    public static Il2CppSystem.Collections.IEnumerator ToIl2Cpp(this IEnumerator enumerator)
    {
        return new ManagedEnumerator(enumerator);
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
        awaiter.OnCompleted(DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(continuation));
    }

    public void GetResult()
    {
        awaiter.GetResult();
    }
}

/// <summary>
/// Lets Unity step a managed iterator through its IL2CPP IEnumerator interface.
/// </summary>
public class ManagedEnumerator : Il2CppSystem.Object, Il2CppSystem.Collections.IEnumerator
{
    private static bool _registered;
    private readonly IEnumerator _inner;

    public ManagedEnumerator(IntPtr pointer) : base(pointer)
    {
    }

    public ManagedEnumerator(IEnumerator inner) : base(Register())
    {
        ClassInjector.DerivedConstructorBody(this);
        _inner = inner;
    }

    private static IntPtr Register()
    {
        if (!_registered)
        {
            ClassInjector.RegisterTypeInIl2Cpp<ManagedEnumerator>();
            _registered = true;
        }

        return ClassInjector.DerivedConstructorPointer<ManagedEnumerator>();
    }

    private Il2CppSystem.Object _current;

    public bool MoveNext()
    {
        if (!_inner.MoveNext())
        {
            _current = null;
            return false;
        }

        // Unity needs an IL2CPP wrapper to recognize a nested managed coroutine.
        _current = _inner.Current switch
        {
            Il2CppSystem.Object il2cppObject => il2cppObject,
            IEnumerator nested => new ManagedEnumerator(nested),
            _ => null
        };
        return true;
    }

    public Il2CppSystem.Object Current
    {
        get
        {
            return _current;
        }
    }

    // The interop interface also inherits managed IEnumerator, whose Current returns System.Object.
    object IEnumerator.Current
    {
        get
        {
            return _current;
        }
    }

    public void Reset()
    {
        _inner.Reset();
    }
}
