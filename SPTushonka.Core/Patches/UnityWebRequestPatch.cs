using System.Reflection;
using HarmonyLib;
using SPTushonka.Core.Models;
using SPTushonka.Reflection.Patching;
using UnityEngine.Networking;

namespace SPTushonka.Core.Patches;

// Texture requests carry their own certificate handler rather than going through
// ClientCertificateHandler, so they need one that accepts the server's self-signed cert.
public class UnityWebRequestPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(UnityWebRequest), nameof(UnityWebRequest.SendWebRequest));
    }

    [PatchPrefix]
    private static void PatchPrefix(UnityWebRequest __instance)
    {
        if (__instance.certificateHandler != null || __instance.downloadHandler?.TryCast<DownloadHandlerTexture>() == null)
        {
            return;
        }

        __instance.certificateHandler = new FakeCertificateHandler();
        __instance.disposeCertificateHandlerOnDispose = true;
        __instance.timeout = 15000;
    }
}
