using Iced.Intel;

namespace SPTushonka.PrePatch.Loader;

// Il2CppInterop's signature scan and xref walk for Class::GetDefaultFieldValue each land on the
// wrong routine on some build. Field::StaticGetValue calls it first thing once it has tested the
// HAS_DEFAULT bit on the field type, which has held on every build so far.
internal static class FieldDefaultFinder
{
    private const int Window = 0x800;
    private const int HasDefault = 0x40;
    private const int MaxHops = 4;

    // RVA of Class::GetDefaultFieldValue, or -1.
    public static int Find(PeView pe)
    {
        if (!pe.TryGetExport("il2cpp_field_static_get_value", out var export))
        {
            return -1;
        }

        var plan = GateDecoder.Analyse(pe, export.Rva);
        int fn = plan?.Impl ?? GateDecoder.SingleJmpTarget(pe, export.Rva);
        for (int hop = 0; hop < MaxHops && fn > 0 && pe.IsExecutable(fn); hop++)
        {
            int found = FirstCallAfterDefaultTest(pe, fn, out int tail);
            if (found > 0)
            {
                return found;
            }

            fn = tail;
        }

        return -1;
    }

    // The call target, or 0 with `tail` set when the function only forwards elsewhere.
    private static int FirstCallAfterDefaultTest(PeView pe, int rva, out int tail)
    {
        tail = -1;
        var code = pe.ReadRva(rva, Window);
        if (code == null)
        {
            return 0;
        }

        bool seenTest = false;
        Instruction last = default;
        foreach (var insn in X64.Function(code, rva))
        {
            last = insn;
            bool testsDefault = insn.Mnemonic == Mnemonic.Test && insn.Op0Kind == OpKind.Memory && insn.MemoryDisplacement64 == 8
                                && X64.IsImmediate(insn.Op1Kind) && insn.GetImmediate(1) == HasDefault;
            if (testsDefault)
            {
                seenTest = true;
            }
            else if (seenTest && insn.FlowControl == FlowControl.Call && X64.IsNear(insn))
            {
                return (int)insn.NearBranch64;
            }
        }

        // A function that ends in a jump without ever testing the bit is a forwarder.
        if (!seenTest && last.FlowControl == FlowControl.UnconditionalBranch && X64.IsNear(last))
        {
            tail = (int)last.NearBranch64;
        }

        return 0;
    }
}
