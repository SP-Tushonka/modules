using System;
using System.IO;
using System.Threading.Tasks;

namespace SPTushonka.Common.Utils;

public static class VFS
{
    public static string Cwd { get; private set; } = Environment.CurrentDirectory;

    public static bool Exists(string filepath)
    {
        return Directory.Exists(filepath) || File.Exists(filepath);
    }

    /// <summary>
    /// Get file content as string.
    /// </summary>
    public static async Task<string> ReadTextFileAsync(string filepath)
    {
        return await File.ReadAllTextAsync(filepath);
    }
}
