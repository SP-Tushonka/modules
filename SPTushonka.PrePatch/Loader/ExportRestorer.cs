using System;
using System.Linq;
using BepInEx.Logging;

namespace SPTushonka.PrePatch.Loader;

// Takes the token gate off every gated il2cpp export. Il2CppInterop imports nearly the whole API,
// so there is no useful subset to limit this to.
internal sealed class ExportRestorer(ManualLogSource log, PeView pe, IntPtr moduleBase)
{
    public void Run()
    {
        int restored = 0;
        int skipped = 0;
        int ungated = 0;
        foreach (var export in pe.Exports)
        {
            if (!export.Name.StartsWith("il2cpp_", StringComparison.Ordinal) || export.Rva <= 0)
            {
                continue;
            }

            var plan = GateDecoder.Analyse(pe, export.Rva);
            if (plan == null)
            {
                ungated++;
                continue;
            }

            if (Apply(plan.Value))
            {
                log.LogDebug($"abi: {export.Name} {plan.Value.Kind}");
                restored++;
            }
            else
            {
                log.LogWarning($"abi: {export.Name} {plan.Value.Kind} write failed, left stock");
                skipped++;
            }
        }

        log.LogMessage($"abi: {restored} gated export(s) restored, {ungated} ungated, {skipped} failed");
        PointExportAtRealBody("il2cpp_runtime_invoke");
    }

    private bool Apply(in GatePlan plan)
    {
        return plan.Kind switch
        {
            GateFix.Accessor => Native.Write(moduleBase + plan.Wrapper, Native.GuardedAccessor(plan.Stub)),
            GateFix.PassThrough => Native.Write(moduleBase + plan.Wrapper, plan.Stub),
            GateFix.InPlace => plan.Writes.Count > 0 && plan.Writes.All(w => Native.Write(moduleBase + w.Rva, w.Bytes)),
            _ => false,
        };
    }

    // A 5 byte thunk with the next function right behind it, so BepInEx's detour on it corrupts
    // the neighbour. The export slot is moved to the real body, which is safe to hook.
    private void PointExportAtRealBody(string name)
    {
        if (!pe.TryGetExport(name, out var export))
        {
            log.LogWarning($"thunk: {name} is not exported");
            return;
        }

        int body = GateDecoder.SingleJmpTarget(pe, export.Rva);
        if (body <= 0 || !pe.IsExecutable(body))
        {
            log.LogDebug($"thunk: {name} is not a thunk, left as is");
            return;
        }

        IntPtr slot = moduleBase + (int)pe.AddressOfFunctionsRva + export.Ordinal * 4;
        if (!Native.WriteInt32(slot, body))
        {
            log.LogWarning($"thunk: could not write the export slot for {name}");
            return;
        }

        log.LogMessage($"thunk: {name} export moved from 0x{export.Rva:X} to its body 0x{body:X}");
    }
}
