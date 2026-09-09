using System.IO;
using System.IO.Compression;

namespace SPTushonka.Common.Http;

public static class Zlib
{
    public static bool IsCompressed(byte[] data)
    {
        if (data == null || data.Length < 2) return false;
        return (data[0] & 0x0F) == 8 && (((data[0] << 8) | data[1]) % 31) == 0;
    }

    public static byte[] Compress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, true))
        {
            zlib.Write(data, 0, data.Length);
        }
        return output.ToArray();
    }

    public static byte[] Decompress(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);
        return output.ToArray();
    }
}
