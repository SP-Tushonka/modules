using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using BepInEx.Logging;
using HarmonyLib;
using Iced.Intel;

namespace SPTushonka.PrePatch.Loader;

// Il2CppInterop finds two internal il2cpp routines by signature or xref walk, and each has landed
// on the wrong function on some build. The finders are postfixed to hand back the right one.
internal static class InteropCorrections
{
    private const string HookNamespace = "Il2CppInterop.Runtime.Injection.Hooks.";
    private const string TypeDefIndexHook = HookNamespace + "MetadataCache_GetTypeInfoFromTypeDefinitionIndex_Hook";
    private const string FieldDefaultValueHook = HookNamespace + "Class_GetFieldDefaultValue_Hook";

    private static ManualLogSource s_log;
    private static PeView s_pe;
    private static IntPtr s_base;

    public static void Install(ManualLogSource log, PeView pe, IntPtr moduleBase)
    {
        s_log = log;
        s_pe = pe;
        s_base = moduleBase;
        WhenInteropType(TypeDefIndexHook, type => PostfixFinder(type, "metadatacache", nameof(CorrectTypeDefIndexTarget)));
        WhenInteropType(FieldDefaultValueHook, type => PostfixFinder(type, "fielddefault", nameof(CorrectFieldDefaultValueTarget)));
    }

    // Il2CppInterop.Runtime may not be loaded yet, so the patch waits for it when needed.
    private static void WhenInteropType(string name, Action<Type> apply)
    {
        var type = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => { try { return assembly.GetType(name); } catch { return null; } })
            .FirstOrDefault(t => t != null);
        if (type != null)
        {
            apply(type);
            return;
        }

        AppDomain.CurrentDomain.AssemblyLoad += (_, e) =>
        {
            if (e.LoadedAssembly.GetName().Name != "Il2CppInterop.Runtime")
            {
                return;
            }

            try
            {
                var loaded = e.LoadedAssembly.GetType(name);
                if (loaded != null)
                {
                    apply(loaded);
                }
            }
            catch (Exception ex)
            {
                s_log.LogError($"interop: deferred patch of {name} failed: {ex}");
            }
        };
    }

    private static void PostfixFinder(Type hookType, string scope, string postfixName)
    {
        try
        {
            var target = hookType.GetMethod("FindTargetMethod", BindingFlags.Public | BindingFlags.Instance);
            if (target == null)
            {
                s_log.LogWarning($"{scope}: FindTargetMethod not found");
                return;
            }

            var postfix = typeof(InteropCorrections).GetMethod(postfixName, BindingFlags.NonPublic | BindingFlags.Static);
            new Harmony($"sptushonka.abi.{scope}").Patch(target, postfix: new HarmonyMethod(postfix));
        }
        catch (Exception ex)
        {
            s_log.LogError($"{scope}: patch failed: {ex}");
        }
    }

    // Leaf routines have no unwind entry, so a miss here is a hint in the log, not a veto.
    private static string StartNote(IntPtr fn)
    {
        long rva = fn.ToInt64() - s_base.ToInt64();
        return rva > 0 && rva < int.MaxValue && s_pe.IsFunctionStart((int)rva) ? "" : " (no unwind entry)";
    }

    // 46911's signature scan matched SortedList.Insert and 47242's xref walk stopped inside an
    // epilogue, so the walk from Field::StaticGetValue is done here instead.
    private static void CorrectFieldDefaultValueTarget(ref IntPtr __result)
    {
        try
        {
            int rva = FieldDefaultFinder.Find(s_pe);
            if (rva <= 0)
            {
                s_log.LogWarning($"fielddefault: walk found nothing, Il2CppInterop's 0x{__result.ToInt64():X} kept{StartNote(__result)}");
                return;
            }

            var real = s_base + rva;
            if (real == __result)
            {
                s_log.LogInfo($"fielddefault: Il2CppInterop and the walk agree on 0x{real.ToInt64():X}");
                return;
            }

            s_log.LogMessage($"fielddefault: Il2CppInterop gave 0x{__result.ToInt64():X}{StartNote(__result)}, using 0x{real.ToInt64():X}{StartNote(real)}");
            __result = real;
        }
        catch (Exception ex)
        {
            s_log.LogError($"fielddefault: correction failed: {ex}");
        }
    }

    // The walk stops on the wrapper taking an Il2CppTypeDefinition pointer where the hook expects
    // an int index, so it truncates the pointer and indexes with garbage.
    private static void CorrectTypeDefIndexTarget(ref IntPtr __result)
    {
        if (__result == IntPtr.Zero)
        {
            s_log.LogWarning("metadatacache: Il2CppInterop resolved nothing to correct");
            return;
        }

        var real = IndexRoutineBehindWrapper(__result);
        if (real == IntPtr.Zero || real == __result)
        {
            s_log.LogInfo($"metadatacache: 0x{__result.ToInt64():X} is the index routine itself{StartNote(__result)}");
            return;
        }

        s_log.LogMessage($"metadatacache: 0x{__result.ToInt64():X} takes a typedef pointer, using 0x{real.ToInt64():X}");
        __result = real;
    }

    // The wrapper turns the pointer into an index with a magic multiply before tail jumping. The
    // index routine has no imul.
    private static IntPtr IndexRoutineBehindWrapper(IntPtr fn)
    {
        try
        {
            var code = new byte[256];
            Marshal.Copy(fn, code, 0, code.Length);
            var decoder = Decoder.Create(64, code, (ulong)fn.ToInt64());
            bool multiplies = false;
            for (int i = 0; i < 64; i++)
            {
                var insn = decoder.Decode();
                if (insn.IsInvalid || insn.FlowControl == FlowControl.Return)
                {
                    break;
                }

                multiplies |= insn.Mnemonic == Mnemonic.Imul;
                if (insn.FlowControl == FlowControl.UnconditionalBranch && insn.Op0Kind == OpKind.NearBranch64)
                {
                    return multiplies ? new IntPtr((long)insn.NearBranch64) : IntPtr.Zero;
                }
            }
        }
        catch (Exception ex)
        {
            s_log.LogError($"metadatacache: decode failed: {ex}");
        }

        return IntPtr.Zero;
    }
}
