using System.Collections.Concurrent;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SPT.Common.Http;
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

    public static string GetBundlePath(BundleItem bundle)
    {
        return RequestHandler.IsLocal ? $"{RuntimePath}{bundle.ModPath}/bundles/" : CachePath;
    }

    public static string GetBundleFilePath(BundleItem bundle)
    {
        return GetBundlePath(bundle) + bundle.FileName;
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
