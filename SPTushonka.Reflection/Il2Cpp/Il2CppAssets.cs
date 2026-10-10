using UnityEngine;

namespace SPTushonka.Reflection.Il2Cpp;

/// <summary>
/// Loads Unity assets by type and marks assets that must survive unused-asset cleanup.
/// </summary>
/// <remarks>
/// An asset a mod still references survives Resources.UnloadUnusedAssets, like on Mono. KeepLoaded is for assets
/// nothing references. Call these helpers on Unity's main thread.
/// </remarks>
public static class Il2CppAssets
{
    /// <summary>
    /// Loads an asset of type T, then casts the returned interop wrapper.
    /// </summary>
    public static T LoadAsset<T>(this AssetBundle bundle, string name) where T : Object
    {
        return bundle.LoadAsset(name, typeof(T))?.Cast<T>();
    }

    /// <summary>
    /// Excludes an asset from Resources.UnloadUnusedAssets even when nothing references it. Returns the same wrapper.
    /// Null is unchanged.
    /// </summary>
    /// <remarks>
    /// This sets a persistent flag on the asset, not a scoped reference. The owner must clear the flag when
    /// appropriate or explicitly unload the asset. It does not prevent Destroy or forced bundle unloading.
    /// </remarks>
    public static T KeepLoaded<T>(this T asset) where T : Object
    {
        if (asset != null)
        {
            asset.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        }

        return asset;
    }
}
