using System;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using BepInEx.Logging;

namespace SPTushonka.Common.Http;

public static class RequestHandler
{
    private static readonly ManualLogSource Log = Logger.CreateLogSource(nameof(RequestHandler));

    public static readonly Client HttpClient;
    public static readonly string Host;
    public static readonly string SessionId;
    public static readonly bool IsLocal;

    static RequestHandler()
    {
        foreach (var arg in Environment.GetCommandLineArgs())
        {
            if (arg.Contains("BackendUrl"))
            {
                // the client is handed this with single quotes, which is not valid JSON
                var json = arg.Replace("-config=", string.Empty).Replace('\'', '"');
                Host = JsonSerializer.Deserialize<ServerConfig>(json).BackendUrl;
            }

            if (arg.Contains("-token="))
            {
                SessionId = arg.Replace("-token=", string.Empty);
            }
        }

        IsLocal = Host != null && (Host.Contains("127.0.0.1") || Host.Contains("localhost"));

        HttpClient = new Client(Host, SessionId);
    }

    private static void ValidateJson(string path, string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            Log.LogError($"[REQUEST FAILED] {path}");
            return;
        }

        Log.LogInfo($"[REQUEST SUCCESSFUL] {path}");
    }

    public static async Task<byte[]> GetDataAsync(string path)
    {
        Log.LogInfo($"[REQUEST]: {path}");
        return await HttpClient.GetAsync(path);
    }

    public static byte[] GetData(string path)
    {
        return Task.Run(() => GetDataAsync(path)).Result;
    }

    public static async Task<string> GetJsonAsync(string path)
    {
        Log.LogInfo($"[REQUEST]: {path}");

        var payload = await HttpClient.GetAsync(path);
        var body = Encoding.UTF8.GetString(payload);

        ValidateJson(path, body);
        return body;
    }

    public static string GetJson(string path)
    {
        return Task.Run(() => GetJsonAsync(path)).Result;
    }

    public static async Task<string> PostJsonAsync(string path, string json)
    {
        Log.LogInfo($"[REQUEST]: {path}");

        var data = await HttpClient.PostAsync(path, Encoding.UTF8.GetBytes(json));
        var body = Encoding.UTF8.GetString(data);

        ValidateJson(path, body);
        return body;
    }

    public static string PostJson(string path, string json)
    {
        return Task.Run(() => PostJsonAsync(path, json)).Result;
    }

    // NOTE: returns status code
    public static async Task<string> PutJsonAsync(string path, string json)
    {
        Log.LogInfo($"[REQUEST]: {path}");

        var data = await HttpClient.PutAsync(path, Encoding.UTF8.GetBytes(json));
        var body = Encoding.UTF8.GetString(data);

        ValidateJson(path, body);
        return body;
    }

    // NOTE: returns status code
    public static string PutJson(string path, string json)
    {
        return Task.Run(() => PutJsonAsync(path, json)).Result;
    }
}
