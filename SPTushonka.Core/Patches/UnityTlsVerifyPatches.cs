using System.Linq;
using System.Reflection;
using HarmonyLib;
using Il2CppMono.Unity;
using SPTushonka.Reflection.Patching;

namespace SPTushonka.Core.Patches;

// 1.1 negotiates TLS through UnityTlsContext, whose verify callback returns the failure flags
// directly instead of going through MobileTlsContext.ValidateCertificate, so
// WebSocketSslValidationPatch never sees the notifier socket. The static callback is inlined into
// its native wrapper, which calls the instance one.
public static class UnityTlsVerifyPatches
{
    public class InstanceVerifyCallbackPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.GetDeclaredMethods(typeof(UnityTlsContext))
                .First(method => method.Name == "VerifyCallback" && !method.IsStatic);
        }

        [PatchPrefix]
        private static bool PatchPrefix(ref UnityTls.unitytls_x509verify_result __result)
        {
            // All certs are valid
            __result = UnityTls.unitytls_x509verify_result.UNITYTLS_X509VERIFY_SUCCESS;

            return false; // Skip original
        }
    }
}
