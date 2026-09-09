using System;
using System.IO;
using System.Reflection;
using EFT;
using EFT.Utilities;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using SPTushonka.Common.Http;
using SPTushonka.Reflection.Patching;
using UnityEngine;

namespace SPTushonka.Custom.Patches;

/// <summary>
/// Serves trader and quest images from the SPT folder, fetching them through our own http client.
/// The client's UnityWebRequest download never accepts the self-signed certificate.
/// </summary>
public class RedirectClientImageRequestsPatch : ModulePatch
{
    private static readonly string _sptPath = Path.Combine(Environment.CurrentDirectory, "SPT_Runtime", "user", "sptappdata");

    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(ClientBackendSession), nameof(ClientBackendSession.LoadTextureWithCache));
    }

    [PatchPrefix]
    public static bool PatchPrefix(string __1, ref Il2CppSystem.Threading.Tasks.Task<Texture2D> __result)
    {
        var url = __1;
        var texture = ResourcesCache.Pop<Texture2D>(url.ConvertToResourceLocation(true));
        if (texture == null)
        {
            var bytes = LoadBytes(url);
            if (bytes != null && bytes.Length > 0)
            {
                // No mip chain, so the texture quality setting cannot pick a blurrier level for the UI
                texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (ImageConversion.LoadImage(texture, (Il2CppStructArray<byte>)bytes, true))
                {
                    texture.filterMode = FilterMode.Bilinear;
                    texture.name = url[(url.LastIndexOf('/') + 1)..];
                }
                else
                {
                    texture = null;
                }
            }
        }

        __result = Il2CppSystem.Threading.Tasks.Task.FromResult<Texture2D>(texture);
        return false;
    }

    private static byte[] LoadBytes(string url)
    {
        var path = Path.Combine(_sptPath, url.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(path))
        {
            return File.ReadAllBytes(path);
        }

        try
        {
            var bytes = RequestHandler.GetData(url);
            if (bytes != null && bytes.Length > 0)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, bytes);
            }

            return bytes;
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Could not download {url}: {ex.Message}");
            return null;
        }
    }
}
