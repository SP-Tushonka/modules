using Comfort.Common;
using EFT;

namespace SPTushonka.Reflection.Utils;

public static class ClientAppUtils
{
    public static ClientApplication<IEftSession> GetClientApp()
    {
        return Singleton<ClientApplication<IEftSession>>.Instance;
    }

    public static TarkovApplication GetMainApp()
    {
        return GetClientApp()?.TryCast<TarkovApplication>();
    }
}
