using System;
using System.Runtime.InteropServices;

namespace SPTushonka.PrePatch.Loader;

internal static class Native
{
    private const uint PageExecuteReadWrite = 0x40;

    [DllImport("kernel32.dll")]
    private static extern bool VirtualProtect(IntPtr address, uint size, uint newProtect, out uint oldProtect);

    public static bool Write(IntPtr at, byte[] code)
    {
        if (!VirtualProtect(at, (uint)code.Length, PageExecuteReadWrite, out uint old))
        {
            return false;
        }

        Marshal.Copy(code, 0, at, code.Length);
        VirtualProtect(at, (uint)code.Length, old, out _);
        return true;
    }

    public static bool WriteInt32(IntPtr at, int value)
    {
        return Write(at, BitConverter.GetBytes(value));
    }

    // Il2CppInterop passes sentinels such as 0xFFFFFFFD that a null check lets through. Windows
    // heaps sit above 4 GB but Wine's do not, so below that only the null page and the top 64 KB
    // count as invalid. Those return 0 instead of faulting.
    public static byte[] GuardedAccessor(byte[] body)
    {
        if (body.Length > 100)
        {
            throw new ArgumentException($"accessor body of {body.Length} bytes is out of rel8 range");
        }

        return
        [
            0x48, 0x89, 0xC8,                    // mov rax, rcx
            0x48, 0xC1, 0xE8, 0x20,              // shr rax, 32
            0x75, 0x10,                          // jnz .body
            0x81, 0xF9, 0x00, 0x00, 0x01, 0x00,  // cmp ecx, 0x10000
            0x72, (byte)(body.Length + 9),       // jb .zero
            0x81, 0xF9, 0x00, 0x00, 0xFF, 0xFF,  // cmp ecx, 0xFFFF0000
            0x73, (byte)(body.Length + 1),       // jae .zero
            .. body,                             // .body:
            0xC3,                                // ret
            0x31, 0xC0, 0xC3,                    // .zero: xor eax, eax ; ret
        ];
    }
}
