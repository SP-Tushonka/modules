using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Tasks;
using SPTushonka.Common.Http;
using SPTushonka.Common.Utils;
using SPTushonka.Custom.Models;

namespace SPTushonka.Custom.Utils;

public static class BundleManager
{
    private const string RuntimePath = "SPT_Runtime/";
    private const string CachePath = RuntimePath + "user/cache/bundles/";
    public static readonly ConcurrentDictionary<string, BundleItem> Bundles;

    static BundleManager()
    {
        Bundles = new ConcurrentDictionary<string, BundleItem>();
    }

    public static string GetBundleFilePath(BundleItem bundle)
    {
        var cachedPath = CachePath + bundle.Crc.ToString("X8") + "/" + bundle.FileName;

        if (VFS.Exists(cachedPath))
        {
            return cachedPath;
        }

        return RuntimePath + bundle.ModPath + "/bundles/" + bundle.FileName;
    }

    public static void DownloadManifest()
    {
        var json = RequestHandler.GetJson("/singleplayer/bundles");
        var bundles = JsonSerializer.Deserialize<BundleItem[]>(json, BundleItem.SerializerOptions);

        foreach (var bundle in bundles)
        {
            Bundles.TryAdd(bundle.FileName, bundle);
        }
    }
}
