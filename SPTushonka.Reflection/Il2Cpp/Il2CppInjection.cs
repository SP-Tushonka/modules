using System;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using Il2CppInterop.Runtime.Injection;
using Il2CppInterop.Runtime.InteropTypes;

namespace SPTushonka.Reflection.Il2Cpp;

/// <summary>
/// Registers managed subclasses with IL2CPP and allocates their native instances.
/// </summary>
/// <remarks>
/// Use these helpers for mod-defined subclasses of IL2CPP types. Registration makes the type visible to the
/// game, allocation creates an instance. Call during main-thread initialization to avoid concurrent registration.
/// </remarks>
public static class Il2CppInjection
{
    /// <summary>
    /// Registers T if needed and allocates its native instance. Pass the pointer to the base constructor,
    /// then call ClassInjector.DerivedConstructorBody(this) in the managed constructor body.
    /// </summary>
    public static IntPtr Allocate<T>() where T : Il2CppObjectBase
    {
        if (!ClassInjector.IsTypeRegisteredInIl2Cpp<T>())
        {
            ClassInjector.RegisterTypeInIl2Cpp<T>();
        }

        return ClassInjector.DerivedConstructorPointer<T>();
    }

    /// <summary>
    /// Attempts to register each class derived from an IL2CPP type that has no open generic parameters.
    /// Base types are processed first. Individual registration failures are logged and do not stop the scan.
    /// </summary>
    /// <remarks>
    /// Use when game code needs to construct mod types before a managed constructor calls Allocate.
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
