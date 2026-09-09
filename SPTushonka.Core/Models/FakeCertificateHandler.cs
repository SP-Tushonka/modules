using System;
using Il2CppInterop.Runtime.Injection;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine.Networking;

namespace SPTushonka.Core.Models;

public class FakeCertificateHandler : CertificateHandler
{
    // Needed because otherwise this crashes during finalising because no GC exists
    public FakeCertificateHandler(IntPtr pointer) : base(pointer)
    {
        ClassInjector.DerivedConstructorBody(this);
    }

    public override bool ValidateCertificate(Il2CppStructArray<byte> certificateData)
    {
        return true;
    }
}
