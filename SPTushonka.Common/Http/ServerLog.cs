using System.Text.Json;

namespace SPTushonka.Common.Http;

// Writes a line to the server console through /singleplayer/log. Level values follow
// Microsoft.Extensions.Logging, which the server's client log route was built against.
public static class ServerLog
{
    public const int Trace = 0;
    public const int Debug = 1;
    public const int Information = 2;
    public const int Warning = 3;
    public const int Error = 4;
    public const int Critical = 5;

    public static void Log(string source, string message, int level = Information)
    {
        var request = new ServerLogRequest
        {
            Source = source,
            Message = message,
            Level = level,
        };

        RequestHandler.PostJson("/singleplayer/log", JsonSerializer.Serialize(request));
    }

    private sealed class ServerLogRequest
    {
        public string Source { get; set; }
        public string Message { get; set; }
        public int Level { get; set; }
    }
}
