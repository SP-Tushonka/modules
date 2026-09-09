using System;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine.Build.Pipeline;

namespace SPTushonka.Custom.Utils;

// Il2CppInterop only gives value types instance fields when they are blittable. BundleDetails
// holds a string and a string[], so its generated struct carries no storage and cannot be built
// from managed code - the fields are written straight into il2cpp memory instead.
internal static unsafe class BundleDetailsFactory
{
    private static readonly IntPtr DetailsClass = Il2CppClassPointerStore<BundleDetails>.NativeClassPtr;

    private static readonly int FileNameOffset = OffsetOf("m_FileName");
    private static readonly int CrcOffset = OffsetOf("m_Crc");
    private static readonly int DependenciesOffset = OffsetOf("m_Dependencies");

    private static int OffsetOf(string field)
    {
        var handle = IL2CPP.GetIl2CppField(DetailsClass, field);
        return (int) IL2CPP.il2cpp_field_get_offset(handle);
    }

    // Puts a bundle into the manifest's own dictionary, so the game resolves its dependencies
    // through its normal lookups instead of anything of ours.
    public static void AddToDictionary(
        Il2CppSystem.Collections.Generic.Dictionary<string, BundleDetails> details,
        string key,
        string fileName,
        uint crc,
        Il2CppStringArray dependencies)
    {
        var setItem = IL2CPP.il2cpp_class_get_method_from_name(
            IL2CPP.il2cpp_object_get_class(details.Pointer), "set_Item", 2);
        if (setItem == IntPtr.Zero)
        {
            throw new InvalidOperationException("manifest dictionary set_Item not found");
        }

        var boxed = Box(fileName, crc, dependencies);

        var args = stackalloc IntPtr[2];
        args[0] = IL2CPP.ManagedStringToIl2Cpp(key);
        args[1] = IL2CPP.il2cpp_object_unbox(boxed);

        var exception = IntPtr.Zero;
        IL2CPP.il2cpp_runtime_invoke(setItem, details.Pointer, (void**) args, ref exception);
        Il2CppException.RaiseExceptionIfNecessary(exception);
    }

    private static IntPtr Box(string fileName, uint crc, Il2CppStringArray dependencies)
    {
        var boxed = IL2CPP.il2cpp_object_new(DetailsClass);

        // Reference fields go through the write barrier: a raw store leaves the collector unaware
        // of the reference, so the string and the dependency array can be freed while the manifest
        // still points at them.
        SetReference(boxed, FileNameOffset, IL2CPP.ManagedStringToIl2Cpp(fileName));
        Marshal.WriteInt32(boxed, CrcOffset, unchecked((int) crc));
        SetReference(boxed, DependenciesOffset, dependencies == null ? IntPtr.Zero : dependencies.Pointer);

        return boxed;
    }

    private static void SetReference(IntPtr instance, int offset, IntPtr value)
    {
        IL2CPP.il2cpp_gc_wbarrier_set_field(instance, IntPtr.Add(instance, offset), value);
    }
}
