using System;
using System.Net.Http;
using System.Threading.Tasks;
using BepInEx.Logging;

namespace SPTushonka.Common.Http;

public class Client(string address, string accountId, int retries = 3)
{
    private static readonly ManualLogSource Log = Logger.CreateLogSource(nameof(Client));

    public HttpClient HttpClient { get; } = new(
        new HttpClientHandler
        {
            // cookies are set in the header instead
            UseCookies = false,
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
        });

    public HttpRequestMessage CreateNewHttpRequest(HttpMethod method, string path)
    {
        return new HttpRequestMessage
        {
            Method = method,
            RequestUri = new Uri(address + path),
            Headers = { { "Cookie", $"PHPSESSID={accountId}" } },
        };
    }

    protected async Task<byte[]> SendAsync(HttpMethod method, string path, byte[] data, bool zipped = true)
    {
        using var request = CreateNewHttpRequest(method, path);

        if (data != null)
        {
            if (zipped) data = Zlib.Compress(data);
            if (Shuffle.RequestNeedsPacking(path)) data = Shuffle.Pack(data);
            request.Content = new ByteArrayContent(data);
        }

        using var response = await HttpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"Http response status code: {response.StatusCode}");
        }

        var body = await response.Content.ReadAsByteArrayAsync();

        if (body != null && body.Length > 0 && Shuffle.ResponseNeedsUnpacking(path))
        {
            body = Shuffle.Unpack(body);
        }

        if (Zlib.IsCompressed(body)) body = Zlib.Decompress(body);

        return body;
    }

    protected async Task<byte[]> SendWithRetriesAsync(HttpMethod method, string path, byte[] data, bool compress = true)
    {
        // NOTE: <= is intentional. 0 is send, 1/2/3 is retry
        for (var i = 0; i <= retries; i++)
        {
            try
            {
                return await SendAsync(method, path, data, compress);
            }
            catch (Exception ex)
            {
                Log.LogError(ex);
                if (i >= retries) throw;
            }
        }

        return null;
    }

    public async Task<byte[]> GetAsync(string path)
    {
        return await SendWithRetriesAsync(HttpMethod.Get, path, null);
    }

    public async Task<byte[]> PostAsync(string path, byte[] data, bool compress = true)
    {
        return await SendWithRetriesAsync(HttpMethod.Post, path, data, compress);
    }

    /// <returns>Returns status code as bytes</returns>
    public async Task<byte[]> PutAsync(string path, byte[] data, bool compress = true)
    {
        return await SendWithRetriesAsync(HttpMethod.Post, path, data, compress);
    }
}
