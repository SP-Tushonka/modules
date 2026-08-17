using System.Collections.Concurrent;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SPT.Common.Http;
using SPT.Common.Utils;
using SPT.Custom.Models;

namespace SPT.Custom.Utils;

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

    public static async Task DownloadManifest()
    {
        var json = await RequestHandler.GetJsonAsync("/singleplayer/bundles");
        var bundles = JsonConvert.DeserializeObject<BundleItem[]>(json);

        foreach (var bundle in bundles)
        {
            Bundles.TryAdd(bundle.FileName, bundle);
        }
    }
}
