using System;
using System.Runtime.InteropServices;
using BepInEx.Logging;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Runtime;

namespace SPTushonka.Core.Patches;

public static class FilesCheckerStubs
{
    private const uint PageExecuteReadWrite = 0x40;
    private const byte Ret = 0xC3;

    private static readonly string[] Methods = ["EnsureConsistency", "EnsureAvailability", "EnsureSize", "EnsureChecksum"];

    [DllImport("kernel32.dll")]
    private static extern bool VirtualProtect(IntPtr address, uint size, uint newProtect, out uint oldProtect);

    public static void Apply(ManualLogSource log)
    {
        IntPtr klass = IL2CPP.GetIl2CppClass("FilesChecker.dll", "FilesChecker", "ConsistencyController");
        if (klass == IntPtr.Zero)
        {
            log.LogWarning("files checker: ConsistencyController not found, checks left active");
            return;
        }

        int done = 0;
        IntPtr iter = IntPtr.Zero;
        IntPtr method;
        while ((method = IL2CPP.il2cpp_class_get_methods(klass, ref iter)) != IntPtr.Zero)
        {
            string name = Marshal.PtrToStringAnsi(IL2CPP.il2cpp_method_get_name(method));
            if (Array.IndexOf(Methods, name) < 0 || !ReturnsVoid(method))
            {
                continue;
            }

            IntPtr body = Marshal.ReadIntPtr(method);
            if (body == IntPtr.Zero || !VirtualProtect(body, 1, PageExecuteReadWrite, out uint old))
            {
                continue;
            }

            Marshal.WriteByte(body, Ret);
            VirtualProtect(body, 1, old, out _);
            log.LogDebug($"files checker: ConsistencyController.{name} stubbed");
            done++;
        }

        log.LogMessage($"files checker: {done} check(s) stubbed");
    }

    private static bool ReturnsVoid(IntPtr method)
    {
        IntPtr type = IL2CPP.il2cpp_method_get_return_type(method);
        return type != IntPtr.Zero && IL2CPP.il2cpp_type_get_type(type) == (int)Il2CppTypeEnum.IL2CPP_TYPE_VOID;
    }
}
