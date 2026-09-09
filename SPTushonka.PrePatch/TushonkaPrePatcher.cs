using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Preloader.Core.Patching;
using SPTushonka.PrePatch.Loader;
using SPTushonka.PrePatch.Tushonka;

namespace SPTushonka.PrePatch;

// The game's il2cpp exports take a token argument and return garbage without it, so Il2CppInterop's
// documented calls all fail. The export entries are rewritten in memory. No game file is modified.
[PatcherPluginInfo("sptushonka.prepatch", "SPTushonka PrePatch", PluginInfo.Version)]
public class SPTushonkaPrePatcher : BasePatcher
{
    // Token Il2Cppmscorlib resolves for Type.op_Equality, the first il2cpp call Harmony's resolver makes.
    private const int TypeOpEqualityToken = 100666354;

    public override void Initialize()
    {
        Logger.Listeners.Add(new LoaderLogListener());
        StartupChecks.Run(Log);
        try
        {
            var gameAssembly = Process.GetCurrentProcess().Modules.Cast<ProcessModule>()
                .FirstOrDefault(m => m.ModuleName == "GameAssembly.dll");
            if (gameAssembly == null || gameAssembly.BaseAddress == IntPtr.Zero)
            {
                Log.LogError("abi: GameAssembly.dll not mapped");
                return;
            }

            Log.LogInfo($"abi: GameAssembly.dll @ 0x{gameAssembly.BaseAddress.ToInt64():X}");
            LogBuild();

            var pe = new PeView(File.ReadAllBytes(gameAssembly.FileName));
            new ExportRestorer(Log, pe, gameAssembly.BaseAddress).Run();

            // Il2CppInterop hands out placeholder methods when these lookups fail and the first Harmony
            // patch then faults inside il2cpp. Better to stop here with the reason in the log.
            var assemblies = Il2CppProbe.AssemblyCount();
            var type = Il2CppProbe.FindClass("mscorlib.dll", "System", "Type");
            if (type == IntPtr.Zero)
            {
                Log.LogFatal($"abi: il2cpp domain lists {assemblies} assemblies and cannot resolve System.Type, the restored exports are not answering. Stopping.");
                Environment.Exit(1);
            }

            var opEquality = Il2CppProbe.FindMethodByToken(type, TypeOpEqualityToken, out var methods);
            if (opEquality == IntPtr.Zero)
            {
                Log.LogFatal($"abi: System.Type lists {methods} methods and none carries token {TypeOpEqualityToken}, Il2CppInterop would invoke a placeholder. Stopping.");
                Environment.Exit(1);
            }

            Log.LogInfo($"abi: il2cpp domain lists {assemblies} assemblies, System.Type has {methods} methods, op_Equality at 0x{opEquality.ToInt64():X}");
            InteropCorrections.Install(Log, pe, gameAssembly.BaseAddress);
        }
        catch (Exception ex)
        {
            Log.LogError($"abi: restore failed: {ex}");
        }
    }

    // The export gate decoder is checked against each new build with gatereport, so the build is
    // logged rather than compared to a list.
    private void LogBuild()
    {
        try
        {
            var exe = Process.GetCurrentProcess().MainModule?.FileName;
            var build = exe == null ? null : FileVersionInfo.GetVersionInfo(exe).ProductVersion?.Trim();
            Log.LogInfo(string.IsNullOrEmpty(build) ? "build: could not read the client version" : $"build: {build}");
        }
        catch (Exception ex)
        {
            Log.LogWarning($"build: version check failed: {ex.Message}");
        }
    }
}
