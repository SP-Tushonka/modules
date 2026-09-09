using System.Collections.Generic;
using System.Linq;
using Iced.Intel;

namespace SPTushonka.PrePatch.Loader;

internal enum GateFix
{
    // The wrapper is one load through the first argument: a guarded stub replaces it.
    Accessor,

    // The wrapper only forwards to a routine: argument setup plus a jump replaces it.
    PassThrough,

    // The routine is inlined into the wrapper: the gate instructions are patched out.
    InPlace,
}

internal readonly struct GatePlan
{
    public GatePlan(int wrapper, int impl, GateFix kind, byte[] stub, List<(int Rva, byte[] Bytes)> writes)
    {
        Wrapper = wrapper;
        Impl = impl;
        Kind = kind;
        Stub = stub;
        Writes = writes;
    }

    // The gated wrapper itself, behind any thunk the export starts with.
    public int Wrapper { get; }

    // The routine a forwarding wrapper hands over to, or -1.
    public int Impl { get; }

    public GateFix Kind { get; }

    // Accessor: the load re-encoded on rcx. PassThrough: the whole replacement wrapper.
    public byte[] Stub { get; }

    // InPlace: the byte writes that take the gate out.
    public List<(int Rva, byte[] Bytes)> Writes { get; }
}

// Every gated il2cpp export takes a 32 byte token after its documented arguments and memcmps it
// before doing anything. A mismatch runs a fail routine that returns junk, a null token returns
// early, and on a match the wrapper either forwards to the real routine or inlines it.
internal static class GateDecoder
{
    private const int Window = 0x600;
    private const int TokenLength = 0x20;
    private const int MaxThunkHops = 4;
    private const int MaxBodyInstructions = 12;

    // Null for ungated exports. A thunk is followed to the wrapper it jumps to.
    public static GatePlan? Analyse(PeView pe, int rva)
    {
        for (int hop = 0; hop < MaxThunkHops; hop++)
        {
            int next = SingleJmpTarget(pe, rva);
            if (next <= 0 || !pe.IsExecutable(next))
            {
                break;
            }

            rva = next;
        }

        var code = pe.ReadRva(rva, Window);
        if (code == null || !FindGate(code, rva, out var before, out var gate, out ulong success))
        {
            return null;
        }

        var copies = ArgCopies(before);
        var passThrough = PassThroughStub(pe, code, rva, success, copies, out int impl);
        var accessor = AccessorBody(code, rva, success, copies);
        if (accessor == null && passThrough != null && passThrough.Length == 5)
        {
            // A wrapper that only jumps keeps the load in the routine behind it.
            var routine = pe.ReadRva(impl, 32);
            accessor = routine == null ? null : AccessorBody(routine, impl, (ulong)impl, ArgCopies([]));
        }

        if (accessor != null)
        {
            return new GatePlan(rva, impl, GateFix.Accessor, accessor, null);
        }

        if (passThrough != null)
        {
            return new GatePlan(rva, impl, GateFix.PassThrough, passThrough, null);
        }

        return new GatePlan(rva, -1, GateFix.InPlace, null, GateRemoval(code, rva, before, gate));
    }

    public static int SingleJmpTarget(PeView pe, int rva)
    {
        var code = pe.ReadRva(rva, 16);
        if (code == null)
        {
            return -1;
        }

        var insn = Decoder.Create(64, code, (ulong)rva).Decode();
        bool jmp = !insn.IsInvalid && insn.FlowControl == FlowControl.UnconditionalBranch && X64.IsNear(insn);
        return jmp ? (int)insn.NearBranch64 : -1;
    }

    // The gate is `test eax,eax` right after the memcmp call that got the token length in r8d.
    // `before` holds everything up to it, `success` is where the token match continues.
    private static bool FindGate(byte[] code, int rva, out List<Instruction> before, out Instruction gate, out ulong success)
    {
        before = [];
        gate = default;
        success = 0;
        Instruction branch = default;
        bool found = false;
        foreach (var insn in X64.Function(code, rva))
        {
            if (found)
            {
                branch = insn;
                break;
            }

            if (IsGate(insn, before))
            {
                gate = insn;
                found = true;
                continue;
            }

            before.Add(insn);
        }

        if (!found || branch.FlowControl != FlowControl.ConditionalBranch || !X64.IsNear(branch))
        {
            return false;
        }

        success = branch.Mnemonic == Mnemonic.Jne ? branch.NextIP : branch.NearBranch64;
        return true;
    }

    private static bool IsGate(in Instruction insn, List<Instruction> before)
    {
        if (insn.Mnemonic != Mnemonic.Test || insn.Op0Register != Register.EAX || insn.Op1Register != Register.EAX)
        {
            return false;
        }

        var recent = before.Skip(before.Count - 6).ToList();
        bool sawCall = recent.Any(p => p.FlowControl == FlowControl.Call);
        bool sawLength = recent.Any(p => p.Mnemonic == Mnemonic.Mov && p.Op0Register == Register.R8D && X64.IsImmediate(p.Op1Kind) && p.GetImmediate(1) == TokenLength);
        return sawCall && sawLength;
    }

    // Which argument register each register holds after the prologue, the arguments themselves included.
    private static Dictionary<Register, Register> ArgCopies(List<Instruction> before)
    {
        Dictionary<Register, Register> copies = X64.ArgRegisters.ToDictionary(r => r, r => r);
        foreach (var insn in before)
        {
            if (insn.Mnemonic != Mnemonic.Mov || insn.Op0Kind != OpKind.Register || insn.Op1Kind != OpKind.Register)
            {
                continue;
            }

            var src = insn.Op1Register.GetFullRegister();
            var dst = insn.Op0Register.GetFullRegister();
            if (copies.TryGetValue(src, out var arg))
            {
                copies[dst] = arg;
            }
            else
            {
                copies.Remove(dst);
            }
        }

        return copies;
    }

    // Drops every jz to the fail routine, drops the calls that derive the expected token, and
    // turns the memcmp into xor eax,eax. A jz straight to ret is the null token return the engine
    // relies on when it calls without a token, so it stays.
    private static List<(int Rva, byte[] Bytes)> GateRemoval(byte[] code, int rva, List<Instruction> before, in Instruction gate)
    {
        int compare = before.FindLastIndex(i => i.FlowControl == FlowControl.Call);
        List<(int Rva, byte[] Bytes)> writes = [];
        for (int i = 0; i < before.Count; i++)
        {
            var insn = before[i];
            bool failBranch = insn.Mnemonic == Mnemonic.Je && X64.IsNear(insn) && insn.NearBranch64 > gate.IP && !X64.ReturnsAt(code, rva, insn.NearBranch64);
            if (failBranch)
            {
                writes.Add(((int)insn.IP, X64.Nops(insn.Length)));
                continue;
            }

            if (insn.FlowControl != FlowControl.Call)
            {
                continue;
            }

            if (i == compare)
            {
                var xorEax = X64.Nops(insn.Length);
                xorEax[0] = 0x31;
                xorEax[1] = 0xC0;
                writes.Add(((int)insn.IP, xorEax));
                continue;
            }

            // A call skipped by the jnz right before it is the thread state initialiser, kept.
            bool guarded = i > 0 && before[i - 1].Mnemonic == Mnemonic.Jne && before[i - 1].NearBranch64 == insn.NextIP;
            if (!guarded)
            {
                writes.Add(((int)insn.IP, X64.Nops(insn.Length)));
            }
        }

        return writes;
    }

    // One load into rax through rcx (or a register the prologue copied it to), optionally followed
    // by register only arithmetic, then the epilogue. The load is re-encoded on rcx.
    private static byte[] AccessorBody(byte[] code, int rva, ulong success, Dictionary<Register, Register> copies)
    {
        var decoder = X64.At(code, rva, success);
        if (decoder == null)
        {
            return null;
        }

        var load = decoder.Decode();
        bool loads = load.Mnemonic is Mnemonic.Mov or Mnemonic.Movzx or Mnemonic.Movsxd or Mnemonic.Lea;
        bool intoRax = load.Op0Kind == OpKind.Register && load.Op0Register is Register.RAX or Register.EAX;
        bool fromRcx = load.Op1Kind == OpKind.Memory && copies.TryGetValue(load.MemoryBase, out var arg) && arg == Register.RCX && load.MemoryIndex == Register.None;
        if (!loads || !intoRax || !fromRcx)
        {
            return null;
        }

        load.MemoryBase = Register.RCX;
        List<byte> body = [.. X64.Encode(load)];
        for (int i = 0; i < MaxBodyInstructions; i++)
        {
            var insn = decoder.Decode();
            if (insn.IsInvalid)
            {
                return null;
            }

            if (insn.FlowControl == FlowControl.Return)
            {
                return body.ToArray();
            }

            if (insn.FlowControl == FlowControl.UnconditionalBranch && X64.IsNear(insn))
            {
                decoder = X64.At(code, rva, insn.NearBranch64);
                if (decoder == null)
                {
                    return null;
                }

                continue;
            }

            if (X64.IsEpilogue(insn))
            {
                continue;
            }

            bool arithmetic = insn.Mnemonic is Mnemonic.And or Mnemonic.Or or Mnemonic.Xor or Mnemonic.Not or Mnemonic.Neg
                              or Mnemonic.Shr or Mnemonic.Shl or Mnemonic.Sar or Mnemonic.Add or Mnemonic.Sub or Mnemonic.Movzx or Mnemonic.Movsxd;
            bool onAccumulator = insn.Op0Kind == OpKind.Register && insn.Op0Register is Register.RAX or Register.EAX or Register.AX or Register.AL;
            bool registerOnly = insn.MemoryBase == Register.None && insn.MemoryIndex == Register.None && insn.Op1Kind != OpKind.Memory;
            if (!arithmetic || !onAccumulator || !registerOnly)
            {
                return null;
            }

            body.AddRange(X64.Raw(code, rva, insn));
        }

        return null;
    }

    // Argument moves and immediates, then one call followed by the epilogue or one tail jump.
    // The stub redoes the setup on the live argument registers and jumps to the routine, so the
    // export stays a single hop for Il2CppInterop's xref scanners.
    private static byte[] PassThroughStub(PeView pe, byte[] code, int rva, ulong success, Dictionary<Register, Register> copies, out int impl)
    {
        impl = -1;
        var decoder = X64.At(code, rva, success);
        if (decoder == null)
        {
            return null;
        }

        List<byte> setup = [];
        bool tail = false;
        for (int i = 0; i < MaxBodyInstructions && impl < 0; i++)
        {
            var insn = decoder.Decode();
            if (insn.IsInvalid)
            {
                return null;
            }

            bool call = insn.FlowControl == FlowControl.Call;
            tail = insn.FlowControl == FlowControl.UnconditionalBranch;
            if (call || tail)
            {
                bool inside = insn.NearBranch64 >= (ulong)rva && insn.NearBranch64 < (ulong)rva + (ulong)code.Length;
                if (!X64.IsNear(insn) || (tail && inside))
                {
                    return null;
                }

                impl = (int)insn.NearBranch64;
                continue;
            }

            if (X64.IsEpilogue(insn))
            {
                continue;
            }

            bool toArg = insn.Op0Kind == OpKind.Register && X64.ArgRegisters.Contains(insn.Op0Register.GetFullRegister());
            if (!toArg)
            {
                return null;
            }

            if (insn.Mnemonic == Mnemonic.Mov && insn.Op1Kind == OpKind.Register)
            {
                // A move from the prologue's copy of the same argument is already true in the stub.
                if (!copies.TryGetValue(insn.Op1Register.GetFullRegister(), out var arg) || arg != insn.Op0Register.GetFullRegister())
                {
                    return null;
                }

                continue;
            }

            bool immediate = (insn.Mnemonic == Mnemonic.Mov && X64.IsImmediate(insn.Op1Kind))
                             || (insn.Mnemonic == Mnemonic.Xor && insn.Op1Kind == OpKind.Register && insn.Op0Register == insn.Op1Register);
            if (!immediate)
            {
                return null;
            }

            setup.AddRange(X64.Raw(code, rva, insn));
        }

        if (impl <= 0 || !pe.IsExecutable(impl) || (!tail && !EpilogueUntilReturn(code, rva, decoder)))
        {
            return null;
        }

        return [.. setup, .. X64.JumpTo(rva + setup.Count, impl)];
    }

    private static bool EpilogueUntilReturn(byte[] code, int rva, Decoder decoder)
    {
        for (int i = 0; i < MaxBodyInstructions; i++)
        {
            var insn = decoder.Decode();
            if (insn.IsInvalid)
            {
                return false;
            }

            if (insn.FlowControl == FlowControl.Return)
            {
                return true;
            }

            if (insn.FlowControl == FlowControl.UnconditionalBranch && X64.IsNear(insn))
            {
                decoder = X64.At(code, rva, insn.NearBranch64);
                if (decoder == null)
                {
                    return false;
                }

                continue;
            }

            if (!X64.IsEpilogue(insn))
            {
                return false;
            }
        }

        return false;
    }
}
