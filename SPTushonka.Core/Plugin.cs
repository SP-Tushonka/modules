using BepInEx;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using SPTushonka.Core.Models;
using SPTushonka.Core.Patches;
using SPTushonka.Core.Patches.Metrics;

using SPTushonka.Reflection.Patching;

namespace SPTushonka.Core;

[BepInPlugin("sptushonka.core", "SPTushonka Core", PluginInfo.Version)]
public class Plugin : BasePlugin
{
    public override void Load()
    {
        ClassInjector.RegisterTypeInIl2Cpp<FakeCertificateHandler>();

        new BattlEyePatch().Enable();
        FilesCheckerStubs.Apply(Log);
        new ValidateAnticheatPatch().Enable();
        new SslCertificatePatch().Enable();
        new UnityWebRequestPatch().Enable();
        new WebSocketSslValidationPatch().Enable();
        new WebSocketLogLevelPatch().Enable();
        UnityTlsVerifyPatches.Patch();
        new RemoveHwInfoPatch().Enable();
        new SendMetricsJsonPatch().Enable();
        new SendMetricsPatch().Enable();
        new SendLoadMetricsPatch().Enable();

        ModulePatch.Summarise("Core");
    }
}
