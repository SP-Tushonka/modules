using System;
using System.Buffers.Binary;

namespace SPTushonka.Common.Http;

// The server permutes every body except the /launcher and /files routes, and does not permute
// its /singleplayer responses. The table is a plain
// LCG walked from a fixed cursor, so each entry can be computed directly instead of being
// carried in shared state the way the server does it.
public static class Shuffle
{
    private const ulong Seed = 0x65F6D;
    private const ulong Mul = 1624453;
    private const ulong Add = 1023920427;
    private const ulong Mod = 0x8ED7A18D;

    private static ulong Entry(int index)
    {
        return ((Mul * (Seed + (ulong) index)) + Add) % Mod;
    }

    public static byte[] Pack(byte[] input)
    {
        var output = new byte[input.Length + 4];
        BinaryPrimitives.WriteInt32LittleEndian(output, input.Length);
        Buffer.BlockCopy(input, 0, output, 4, input.Length);

        var startEntry = output.Length % 0xAAB;
        for (var offset = output.Length - 1; offset > 0; offset--)
        {
            var swap = (int) (Entry(offset + startEntry) % (ulong) offset);
            (output[offset], output[swap]) = (output[swap], output[offset]);
        }

        return output;
    }

    public static byte[] Unpack(byte[] input)
    {
        if (input.Length < 4) return input;

        var buffer = (byte[]) input.Clone();
        var startEntry = buffer.Length % 0xAAB;
        for (var offset = 1; offset < buffer.Length; offset++)
        {
            var swap = (int) (Entry(offset + startEntry) % (ulong) offset);
            (buffer[offset], buffer[swap]) = (buffer[swap], buffer[offset]);
        }

        var size = BinaryPrimitives.ReadInt32LittleEndian(buffer);
        if (size < 0 || size > buffer.Length - 4)
        {
            throw new InvalidOperationException($"deshuffle produced a bad length ({size})");
        }

        var result = new byte[size];
        Buffer.BlockCopy(buffer, 4, result, 0, size);
        return result;
    }

    public static bool RequestNeedsPacking(string path)
    {
        return !path.StartsWith("/launcher", StringComparison.Ordinal)
            && !path.StartsWith("/client/metadata", StringComparison.Ordinal)
            && !path.StartsWith("/files", StringComparison.Ordinal);
    }

    public static bool ResponseNeedsUnpacking(string path)
    {
        return !path.StartsWith("/launcher", StringComparison.Ordinal)
            && !path.StartsWith("/singleplayer", StringComparison.Ordinal)
            && !path.StartsWith("/files", StringComparison.Ordinal);
    }
}
