using System;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using Il2CppInterop.Runtime.Injection;
using Il2CppInterop.Runtime.InteropTypes;

namespace SPTushonka.Reflection.Il2Cpp;

/// <summary>
/// Registers managed subclasses with IL2CPP ahead of time.
/// </summary>
/// <remarks>
/// A mod type registers itself the first time managed code uses it, through new, AddComponent or typeof. Ahead of
/// time registration is only needed when game code creates a mod type first. Call during main-thread
/// initialization to avoid concurrent registration.
/// </remarks>
public static class Il2CppInjection
{
    /// <summary>
    /// Attempts to register each class derived from an IL2CPP type that has no open generic parameters.
    /// Base types are processed first. Individual registration failures are logged and do not stop the scan.
    /// </summary>
    /// <remarks>
    /// Use when game code needs to construct mod types before managed code first uses them.
    /// Supply the mod assembly, rather than an assembly containing generated game wrappers.
    /// </remarks>
    public static void RegisterAll(Assembly assembly, ManualLogSource logger)
    {
        var injectable = assembly.GetTypes()
            .Where(t => t.IsClass && !t.ContainsGenericParameters && typeof(Il2CppObjectBase).IsAssignableFrom(t))
            .OrderBy(Depth);

        var failed = 0;
        foreach (var type in injectable)
        {
            try
            {
                if (!ClassInjector.IsTypeRegisteredInIl2Cpp(type))
                {
                    ClassInjector.RegisterTypeInIl2Cpp(type);
                }
            }
            catch (Exception ex)
            {
                failed++;
                logger.LogError($"Could not register {type.FullName} in il2cpp: {ex}");
            }
        }

        if (failed > 0)
        {
            logger.LogError($"{failed} type(s) failed to register in il2cpp");
        }
    }

    private static int Depth(Type type)
    {
        var depth = 0;
        for (var t = type.BaseType; t != null; t = t.BaseType)
        {
            depth++;
        }

        return depth;
    }
}
