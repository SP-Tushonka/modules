using System;
using System.Reflection;
using FilesChecker;
using HarmonyLib;
using SPTushonka.Reflection.Patching;
using CancellationToken = Il2CppSystem.Threading.CancellationToken;

namespace SPTushonka.Core.Patches;

public static class FilesCheckerPatches
{
    public static void Patch()
    {
        new EnsureConsistencyPatch().Enable();
        new EnsureAvailabilityPatch().Enable();
        new EnsureSizePatch().Enable();
        new EnsureChecksumPatch().Enable();
    }

    private static MethodInfo Target(string name, params Type[] parameters)
    {
        var method = AccessTools.DeclaredMethod(typeof(ConsistencyController), name, parameters);
        if (method == null || method.IsStatic || method.ReturnType != typeof(void))
        {
            throw new MissingMethodException($"Expected instance void ConsistencyController.{name}");
        }

        return method;
    }

    public class EnsureConsistencyPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            // Select only the per-file void overload; async result creation stays active.
            return Target(nameof(ConsistencyController.EnsureConsistency),
                typeof(FileConsistencyMetadata), typeof(ConsistencyEnsuranceMode), typeof(CancellationToken));
        }

        [PatchPrefix]
        private static bool PatchPrefix()
        {
            return false;
        }
    }

    public class EnsureAvailabilityPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return Target(nameof(ConsistencyController.EnsureAvailability), typeof(FileConsistencyMetadata));
        }

        [PatchPrefix]
        private static bool PatchPrefix()
        {
            return false;
        }
    }

    public class EnsureSizePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return Target(nameof(ConsistencyController.EnsureSize), typeof(FileConsistencyMetadata));
        }

        [PatchPrefix]
        private static bool PatchPrefix()
        {
            return false;
        }
    }

    public class EnsureChecksumPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return Target(nameof(ConsistencyController.EnsureChecksum), typeof(FileConsistencyMetadata), typeof(CancellationToken));
        }

        [PatchPrefix]
        private static bool PatchPrefix()
        {
            return false;
        }
    }
}
