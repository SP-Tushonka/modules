using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using BepInEx.Logging;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace SPTushonka.Custom.MemoryImprovements;

// EnumHelper<T> converts values through Expression.Compile delegates, which il2cpp runs in the LINQ interpreter.
// This causes a massive memory spike of a few mb/s, this class undoes those allocations.
public static class EnumConverterStubs
{
    private const uint PageExecuteReadWrite = 0x40;

    // One enum per shared instantiation. Bodies are movsxd rax,ecx / movsx / movzx followed by ret.
    private static readonly (string Enum, byte[] ToLong, byte[] ToInt)[] Instantiations =
    [
        ("EFT.EBodyModelPart", [0x48, 0x63, 0xC1, 0xC3], [0x8B, 0xC1, 0xC3]),
        ("PlaceForCheckType", [0x48, 0x0F, 0xBF, 0xC1, 0xC3], [0x0F, 0xBF, 0xC1, 0xC3]),
        ("EPlayerState", [0x0F, 0xB6, 0xC1, 0xC3], [0x0F, 0xB6, 0xC1, 0xC3]),
    ];

    [DllImport("kernel32.dll")]
    private static extern bool VirtualProtect(IntPtr address, uint size, uint newProtect, out uint oldProtect);

    public static void Apply(ManualLogSource log)
    {
        var helper = Il2CppSystem.Type.GetType("EnumHelper`1, Assembly-CSharp");
        if (helper == null)
        {
            log.LogWarning("enum converters: EnumHelper`1 not found, converters left interpreted");
            return;
        }

        long gameAssembly = GameAssemblyBase();
        int done = 0;
        foreach (var (enumName, toLong, toInt) in Instantiations)
        {
            var enumType = Il2CppSystem.Type.GetType($"{enumName}, Assembly-CSharp");
            if (enumType == null)
            {
                log.LogWarning($"enum converters: {enumName} not found");
                continue;
            }

            var arguments = new Il2CppReferenceArray<Il2CppSystem.Type>(1);
            arguments[0] = enumType;
            IntPtr klass = IL2CPP.il2cpp_class_from_system_type(helper.MakeGenericType(arguments).Pointer);

            IntPtr iter = IntPtr.Zero;
            IntPtr method;
            while ((method = IL2CPP.il2cpp_class_get_methods(klass, ref iter)) != IntPtr.Zero)
            {
                string name = Marshal.PtrToStringAnsi(IL2CPP.il2cpp_method_get_name(method));
                byte[] body = name switch
                {
                    "GetLongFromValue" => toLong,
                    "GetIntFromValue" => toInt,
                    _ => null
                };
                if (body == null || IL2CPP.il2cpp_method_get_param_count(method) != 1)
                {
                    continue;
                }

                IntPtr code = Marshal.ReadIntPtr(method);
                if (code == IntPtr.Zero || !VirtualProtect(code, (uint)body.Length, PageExecuteReadWrite, out uint old))
                {
                    log.LogWarning($"enum converters: EnumHelper<{enumName}>.{name} has no writable body");
                    continue;
                }

                Marshal.Copy(body, 0, code, body.Length);
                VirtualProtect(code, (uint)body.Length, old, out _);
                log.LogDebug($"enum converters: EnumHelper<{enumName}>.{name} at RVA 0x{(long)code - gameAssembly:X}");
                done++;
            }
        }

        log.LogMessage($"enum converters: {done} of 6 shared converter(s) replaced");
    }

    private static long GameAssemblyBase()
    {
        foreach (ProcessModule module in Process.GetCurrentProcess().Modules)
        {
            if (string.Equals(module.ModuleName, "GameAssembly.dll", StringComparison.OrdinalIgnoreCase))
            {
                return (long)module.BaseAddress;
            }
        }

        return 0;
    }
}
