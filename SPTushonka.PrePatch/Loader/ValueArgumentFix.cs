using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Logging;
using HarmonyLib;
using Il2CppInterop.Runtime;

namespace SPTushonka.PrePatch.Loader;

internal static class ValueArgumentFix
{
    // Target: EmitConvertArgumentToManaged in the bundled HarmonySupport source revision.
    // Its nullable (line 394) and ordinary (line 408) paths select Ldarg solely on process bitness.
    // https://github.com/BepInEx/Il2CppInterop/blob/dbda1cb353b0f4253345dc45136d170b9e50a5a0/Il2CppInterop.HarmonySupport/Il2CppDetourMethodPatcher.cs#L366-L412
    // We replace those two selections with a native-size decision for Windows x64.

    public static void Install(ManualLogSource log)
    {
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess)
        {
            return;
        }

        var support = Assembly.Load("Il2CppInterop.HarmonySupport");
        var target = support.GetType("Il2CppInterop.HarmonySupport.Il2CppDetourMethodPatcher", true)
            .GetMethod("EmitConvertArgumentToManaged", BindingFlags.NonPublic | BindingFlags.Static);
        if (target == null || target.GetParameters().Length != 4 || target.GetParameters()[2].ParameterType != typeof(Type))
        {
            throw new MissingMethodException("valueargs: unexpected emitter signature");
        }

        new Harmony("sptushonka.abi.valueargs").Patch(target,
            transpiler: new HarmonyMethod(typeof(ValueArgumentFix).GetMethod(nameof(Transpile), BindingFlags.NonPublic | BindingFlags.Static)));
        log.LogMessage("valueargs: corrected both Windows x64 value-argument boxing branches (1/2/4/8 bytes direct)");
    }

    private static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.ToList();
        var is64BitGetter = typeof(Environment).GetProperty(nameof(Environment.Is64BitProcess)).GetMethod;
        var boxingBranches = Enumerable.Range(0, code.Count).Where(index => code[index].Calls(is64BitGetter)).ToArray();
        if (boxingBranches.Length != 2)
        {
            throw new InvalidOperationException("valueargs: expected exactly two stock boxing branches");
        }

        // Validate both nullable and ordinary boxing paths before changing either.
        foreach (var branchStart in boxingBranches)
        {
            ValidateBoxingBranch(code, branchStart);
        }

        var selectLoadOpcode = typeof(ValueArgumentFix).GetMethod(nameof(SelectBoxingLoadOpcode), BindingFlags.NonPublic | BindingFlags.Static);
        foreach (var branchStart in boxingBranches)
        {
            // Patch the managed emitter, which generates the native detour's IL:
            // replace its architecture-based selection with SelectBoxingLoadOpcode(type).
            // Argument 2 is the emitter's Type parameter, not a game method argument.
            // Edit in place so Harmony's labels and exception blocks remain attached.
            code[branchStart].opcode = OpCodes.Ldarg_2;
            code[branchStart].operand = null;
            code[branchStart + 1].opcode = OpCodes.Call;
            code[branchStart + 1].operand = selectLoadOpcode;
            for (var index = branchStart + 2; index <= branchStart + 4; index++)
            {
                code[index].opcode = OpCodes.Nop;
                code[index].operand = null;
            }
        }
        return code;
    }

    private static void ValidateBoxingBranch(List<CodeInstruction> code, int branchStart)
    {
        // Expected stock selection (the call at branchStart was already matched):
        //   call Environment.Is64BitProcess
        //   brtrue value
        //   ldsfld OpCodes.Ldarga_S
        //   br join
        // value: ldsfld OpCodes.Ldarg
        // join:  ...
        // Harmony may expand short branches, so accept both encodings.
        if (branchStart + 5 < code.Count)
        {
            var branchToValue = code[branchStart + 1];
            var loadAddressOpcode = code[branchStart + 2];
            var branchToJoin = code[branchStart + 3];
            var loadValueOpcode = code[branchStart + 4];
            var join = code[branchStart + 5];

            var selectsValueOn64Bit = BranchesTo(branchToValue, loadValueOpcode, OpCodes.Brtrue_S, OpCodes.Brtrue);
            var loadsAddressOtherwise = LoadsOpcodeField(loadAddressOpcode, nameof(OpCodes.Ldarga_S));
            var skipsValueAfterAddress = BranchesTo(branchToJoin, join, OpCodes.Br_S, OpCodes.Br);
            var loadsValueOn64Bit = LoadsOpcodeField(loadValueOpcode, nameof(OpCodes.Ldarg));

            if (selectsValueOn64Bit && loadsAddressOtherwise && skipsValueAfterAddress && loadsValueOn64Bit)
            {
                return;
            }
        }

        throw new InvalidOperationException("valueargs: unexpected stock opcode selection: " +
            string.Join("; ", code.Skip(branchStart).Take(6).Select(instruction => instruction.ToString())));
    }

    private static bool LoadsOpcodeField(CodeInstruction instruction, string fieldName)
    {
        if (instruction.opcode != OpCodes.Ldsfld || instruction.operand is not FieldInfo field)
        {
            return false;
        }

        return field.DeclaringType == typeof(OpCodes) && field.Name == fieldName;
    }

    private static bool BranchesTo(CodeInstruction branch, CodeInstruction destination, OpCode shortForm, OpCode longForm)
    {
        if (branch.opcode != shortForm && branch.opcode != longForm)
        {
            return false;
        }

        return branch.operand is Label label && destination.labels.Contains(label);
    }

    private static OpCode SelectBoxingLoadOpcode(Type argumentType)
    {
        // Windows x64 passes 1/2/4/8-byte aggregates as integer values. Boxing
        // needs their address; treating CancellationToken.None as a pointer
        // instead makes il2cpp_value_box read through zero. Larger aggregates
        // are already indirect. Use native size, not the managed wrapper size.
        var nativeClass = Il2CppClassPointerStore.GetNativeClassPointer(argumentType);
        if (nativeClass == IntPtr.Zero)
        {
            throw new TypeLoadException(argumentType.FullName);
        }
        uint alignment = 0;
        var nativeSize = IL2CPP.il2cpp_class_value_size(nativeClass, ref alignment);
        if (nativeSize <= 0)
        {
            throw new InvalidOperationException("valueargs: invalid native value size for " + argumentType.FullName);
        }
        return nativeSize is 1 or 2 or 4 or 8 ? OpCodes.Ldarga : OpCodes.Ldarg;
    }
}
