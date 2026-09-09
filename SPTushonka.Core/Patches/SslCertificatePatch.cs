using System.Reflection;
using EFT;
using HarmonyLib;
using SPTushonka.Reflection.Patching;
using X509Certificate = Il2CppSystem.Security.Cryptography.X509Certificates.X509Certificate;

namespace SPTushonka.Core.Patches;

public class SslCertificatePatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(
            typeof(ClientCertificateHandler),
            nameof(ClientCertificateHandler.ValidateCertificate),
            new[] { typeof(X509Certificate) }
        );
    }

    [PatchPrefix]
    private static bool PatchPrefix(ref bool __result)
    {
        __result = true;
        return false; // Skip original
    }
}
