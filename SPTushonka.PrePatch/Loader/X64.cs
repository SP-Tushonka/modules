using System.Collections.Generic;
using System.Linq;
using Iced.Intel;

namespace SPTushonka.PrePatch.Loader;

internal static class X64
{
    public static readonly Register[] ArgRegisters = [Register.RCX, Register.RDX, Register.R8, Register.R9];

    // A decoder positioned at `ip`, or null when it falls outside `code`.
    public static Decoder At(byte[] code, int rva, ulong ip)
    {
        int at = (int)(ip - (ulong)rva);
        return at < 0 || at >= code.Length ? null : Decoder.Create(64, code.Skip(at).ToArray(), ip);
    }

    // The instructions of the function starting at `rva`. Decoding stops after a return or an
    // unconditional jump that no earlier branch reaches past, which is where the function ends.
    public static IEnumerable<Instruction> Function(byte[] code, int rva)
    {
        var decoder = Decoder.Create(64, code, (ulong)rva);
        ulong start = (ulong)rva;
        ulong limit = start + (ulong)code.Length;
        ulong furthestBranch = 0;
        while (decoder.IP < limit)
        {
            var insn = decoder.Decode();
            if (insn.IsInvalid)
            {
                yield break;
            }

            if (IsNear(insn) && insn.NearBranch64 >= start && insn.NearBranch64 < limit && insn.NearBranch64 > furthestBranch)
            {
                furthestBranch = insn.NearBranch64;
            }

            yield return insn;
            bool leaves = insn.FlowControl == FlowControl.Return || insn.FlowControl == FlowControl.UnconditionalBranch;
            if (leaves && decoder.IP > furthestBranch)
            {
                yield break;
            }
        }
    }

    public static bool IsNear(in Instruction insn)
    {
        return insn.Op0Kind == OpKind.NearBranch64;
    }

    public static bool IsImmediate(OpKind kind)
    {
        return kind >= OpKind.Immediate8 && kind <= OpKind.Immediate32to64;
    }

    // Callee-saved restores and the stack pointer adjustment before a return.
    public static bool IsEpilogue(in Instruction insn)
    {
        return insn.Mnemonic == Mnemonic.Pop
               || (insn.Mnemonic == Mnemonic.Add && insn.Op0Register == Register.RSP)
               || (insn.Mnemonic == Mnemonic.Mov && insn.Op1Kind == OpKind.Memory && insn.MemoryBase == Register.RSP);
    }

    public static bool ReturnsAt(byte[] code, int rva, ulong ip)
    {
        var decoder = At(code, rva, ip);
        return decoder != null && decoder.Decode().FlowControl == FlowControl.Return;
    }

    public static byte[] Raw(byte[] code, int rva, in Instruction insn)
    {
        return code.Skip((int)(insn.IP - (ulong)rva)).Take(insn.Length).ToArray();
    }

    public static byte[] Encode(Instruction insn)
    {
        var writer = new ByteWriter();
        Encoder.Create(64, writer).Encode(insn, insn.IP);
        return writer.Bytes.ToArray();
    }

    public static byte[] Nops(int length)
    {
        return Enumerable.Repeat((byte)0x90, length).ToArray();
    }

    public static byte[] JumpTo(int fromRva, int toRva)
    {
        return [0xE9, .. System.BitConverter.GetBytes(toRva - (fromRva + 5))];
    }

    private sealed class ByteWriter : CodeWriter
    {
        public List<byte> Bytes { get; } = [];

        public override void WriteByte(byte value)
        {
            Bytes.Add(value);
        }
    }
}
