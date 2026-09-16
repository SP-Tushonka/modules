using System.Linq;
using System.Reflection;
using HarmonyLib;
using Il2CppMono.Unity;
using SPTushonka.Reflection.Patching;

namespace SPTushonka.Core.Patches;

// 1.1 negotiates TLS through UnityTlsContext, whose verify callback returns the failure flags
// directly instead of going through MobileTlsContext.ValidateCertificate, so
// WebSocketSslValidationPatch never sees the notifier socket. Both overloads are covered because
// the static one is the native entry point and may have the instance one inlined into it.
public static class UnityTlsVerifyPatches
{
    public class StaticVerifyCallbackPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.GetDeclaredMethods(typeof(UnityTlsContext))
                .First(method => method.Name == "VerifyCallback" && method.IsStatic);
        }

        [PatchPrefix]
        private static bool PatchPrefix(ref UnityTls.unitytls_x509verify_result __result)
        {
            // All certs are valid
            __result = UnityTls.unitytls_x509verify_result.UNITYTLS_X509VERIFY_SUCCESS;

            return false; // Skip original
        }
    }

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
