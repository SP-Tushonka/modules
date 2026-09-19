using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace SPTushonka.Reflection.Il2Cpp;

/// <summary>
/// Dispatches managed callbacks and captured async continuations to Unity's main thread.
/// </summary>
/// <remarks>
/// IL2CPP's synchronization context does not provide a CLR context for managed mods.
/// After installation, managed awaits that capture this context resume through the dispatcher.
/// Task.Run and ConfigureAwait(false) do not guarantee main-thread execution.
/// </remarks>
public static class MainThread
{
    private static readonly object QueueLock = new();
    private static readonly Queue<WorkItem> Queue = new();
    private static readonly CancellationTokenSource Shutdown = new();
    private static readonly ManualLogSource Logger = BepInEx.Logging.Logger.CreateLogSource("MainThread");
    private static int _threadId;
    private static Context _context;
    private static SynchronizationContext _previousContext;
    private static bool _stopping;

    /// <summary>
    /// Whether the caller is on the thread that installed the dispatcher.
    /// </summary>
    public static bool IsCurrent => Environment.CurrentManagedThreadId == Volatile.Read(ref _threadId);

    /// <summary>
    /// Canceled on the main thread when the dispatcher is destroyed or the application quits.
    /// Registered callbacks must finish synchronously without waiting for queued work.
    /// </summary>
    public static CancellationToken ShutdownToken => Shutdown.Token;

    /// <summary>
    /// Installs the managed context and its Unity component. Call from the main thread during plugin load.
    /// Repeated calls on that thread do nothing. Installation after shutdown is not supported.
    /// </summary>
    public static void Install(BasePlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);

        lock (QueueLock)
        {
            ThrowIfStopping();
            if (_context != null)
            {
                VerifyAccess();
                return;
            }

            if (!ClassInjector.IsTypeRegisteredInIl2Cpp<MainThreadDispatcher>())
            {
                ClassInjector.RegisterTypeInIl2Cpp<MainThreadDispatcher>();
            }

            // Publish the context only after the component was created successfully.
            plugin.AddComponent<MainThreadDispatcher>();
            _previousContext = SynchronizationContext.Current;
            _threadId = Environment.CurrentManagedThreadId;
            _context = new Context();
            SynchronizationContext.SetSynchronizationContext(_context);
        }
    }

    /// <summary>
    /// Queues an action for a dispatcher update. Actions posted during an update wait until a later update.
    /// Exceptions are logged. Pending actions are discarded on shutdown. Later posts throw.
    /// </summary>
    public static void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        Enqueue(new WorkItem(static state => ((Action)state)(), action));
    }

    /// <summary>
    /// Throws unless the dispatcher is running and the caller is on its main thread.
    /// </summary>
    public static void VerifyAccess()
    {
        lock (QueueLock)
        {
            ThrowIfUnavailable();
            if (!IsCurrent)
            {
                throw new InvalidOperationException("This operation must run on the Unity main thread.");
            }
        }
    }

    /// <summary>
    /// Attaches a managed worker thread to IL2CPP if needed. This permits IL2CPP runtime access,
    /// but does not make Unity APIs safe to call from that thread.
    /// </summary>
    public static void AttachToIl2Cpp()
    {
        if (IL2CPP.il2cpp_thread_current() != IntPtr.Zero)
        {
            return;
        }

        IL2CPP.il2cpp_thread_attach(IL2CPP.il2cpp_domain_get());
        // Mark managed workers as background threads so IL2CPP does not wait for them during shutdown.
        Il2CppSystem.Threading.Thread.CurrentThread.IsBackground = true;
    }

    internal static bool TryPost(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        lock (QueueLock)
        {
            if (_stopping)
            {
                return false;
            }

            ThrowIfUnavailable();
            Queue.Enqueue(new WorkItem(static state => ((Action)state)(), action));
            return true;
        }
    }

    private static void Enqueue(WorkItem work)
    {
        lock (QueueLock)
        {
            ThrowIfUnavailable();
            Queue.Enqueue(work);
        }
    }

    private static void ThrowIfStopping()
    {
        if (_stopping)
        {
            throw new OperationCanceledException("The main-thread dispatcher has stopped.", ShutdownToken);
        }
    }

    private static void ThrowIfUnavailable()
    {
        ThrowIfStopping();
        if (_context == null)
        {
            throw new InvalidOperationException("Install the main-thread dispatcher before posting work.");
        }
    }

    private static void Stop()
    {
        lock (QueueLock)
        {
            if (_stopping)
            {
                return;
            }

            _stopping = true;
            while (Queue.TryDequeue(out var work))
            {
                work.Completion?.TrySetCanceled(ShutdownToken);
            }
        }

        try
        {
            // Cancel task bridges while still on the main thread, before IL2CPP shuts down.
            Shutdown.Cancel();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex);
        }
        finally
        {
            if (ReferenceEquals(SynchronizationContext.Current, _context))
            {
                SynchronizationContext.SetSynchronizationContext(_previousContext);
            }
        }
    }

    private sealed class WorkItem(SendOrPostCallback callback, object state, TaskCompletionSource<object> completion = null)
    {
        public TaskCompletionSource<object> Completion { get; } = completion;

        public void Execute()
        {
            try
            {
                callback(state);
                Completion?.TrySetResult(null);
            }
            catch (Exception ex)
            {
                if (Completion != null)
                {
                    Completion.TrySetException(ex);
                }
                else
                {
                    Logger.LogError(ex);
                }
            }
        }
    }

    private sealed class Context : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object state)
        {
            ArgumentNullException.ThrowIfNull(d);
            Enqueue(new WorkItem(d, state));
        }

        public override void Send(SendOrPostCallback d, object state)
        {
            ArgumentNullException.ThrowIfNull(d);
            if (IsCurrent)
            {
                VerifyAccess();
                d(state);
                return;
            }

            // The waiting caller receives callback failures or shutdown cancellation.
            // Never block the main thread waiting for a worker that is itself calling Send.
            TaskCompletionSource<object> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Enqueue(new WorkItem(d, state, completion));
            completion.Task.GetAwaiter().GetResult();
        }

        public override SynchronizationContext CreateCopy() => this;
    }

    /// <summary>
    /// Executes queued managed work during Unity updates and cancels pending work when it stops.
    /// </summary>
    internal class MainThreadDispatcher : MonoBehaviour
    {
        public MainThreadDispatcher(IntPtr pointer) : base(pointer)
        {
        }

        public void Update()
        {
            int pending;
            lock (QueueLock)
            {
                if (_stopping)
                {
                    return;
                }

                pending = Queue.Count;
            }

            if (SynchronizationContext.Current != _context)
            {
                SynchronizationContext.SetSynchronizationContext(_context);
            }

            // Bound this update to the initial queue length so Task.Yield cannot loop within one frame.
            for (var i = 0; i < pending; i++)
            {
                WorkItem work;
                lock (QueueLock)
                {
                    if (!Queue.TryDequeue(out work))
                    {
                        break;
                    }
                }

                work.Execute();
            }
        }

        public void OnApplicationQuit() => Stop();

        public void OnDestroy() => Stop();
    }
}
