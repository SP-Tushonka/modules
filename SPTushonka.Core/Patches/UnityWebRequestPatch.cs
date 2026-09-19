using System.Reflection;
using HarmonyLib;
using SPTushonka.Core.Models;
using SPTushonka.Reflection.Il2Cpp;
using SPTushonka.Reflection.Patching;
using UnityEngine.Networking;

namespace SPTushonka.Core.Patches;

// Texture requests carry their own certificate handler rather than going through
// ClientCertificateHandler, so they need one that accepts the server's self-signed cert.
public class UnityWebRequestPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(UnityWebRequestTexture), nameof(UnityWebRequestTexture.GetTexture),
            new[] { typeof(string) });
    }

    [PatchPostfix]
    private static void PatchPostfix(UnityWebRequest __result)
    {
        __result.certificateHandler = new FakeCertificateHandler(Il2CppInjection.Allocate<FakeCertificateHandler>());
        __result.disposeCertificateHandlerOnDispose = true;
        __result.timeout = 15000;
    }
}
