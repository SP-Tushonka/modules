using System.Reflection;
using HarmonyLib;
using Il2CppMono.Net.Security;
using SPTushonka.Reflection.Patching;
using X509Certificate2 = Il2CppSystem.Security.Cryptography.X509Certificates.X509Certificate2;
using X509Chain = Il2CppSystem.Security.Cryptography.X509Certificates.X509Chain;

namespace SPTushonka.Core.Patches;

/// <summary>
///     Forces Unity to ignore SSL validation failures for the notifier WebSocket. The socket
///     handler makes no external call to validate, so there is no property to set - the check
///     inside Mono's TLS context has to be replaced directly.
///
///     Without this the server never logs a [WS] connect and the client re-creates its notifier
///     channel every 60 seconds, so trader mail never arrives without a restart.
/// </summary>
public class WebSocketSslValidationPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(MobileTlsContext), nameof(MobileTlsContext.ValidateCertificate), [typeof(X509Certificate2), typeof(X509Chain)]);
    }

    [PatchPrefix]
    public static bool PatchPrefix(ref bool __result)
    {
        // All certs are valid
        __result = true;
        return false; // Skip original
    }
}
