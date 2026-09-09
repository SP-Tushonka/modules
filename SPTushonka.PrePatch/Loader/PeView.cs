using System;
using System.Collections.Generic;
using System.Text;

namespace SPTushonka.PrePatch.Loader;

// Reads the file on disk rather than the mapped module, so the code seen is the game's own
// before anything rewrote it in memory.
internal sealed class PeView
{
    public readonly struct Export
    {
        public Export(string name, int ordinal, int rva)
        {
            Name = name;
            Ordinal = ordinal;
            Rva = rva;
        }

        public string Name { get; }
        public int Ordinal { get; }
        public int Rva { get; }
    }

    private const uint ImageScnMemExecute = 0x20000000;

    private readonly byte[] _bytes;
    private readonly (uint Va, uint Span, uint RawPtr, uint Chars)[] _sections;
    private readonly List<Export> _exports = [];
    private uint[] _functionStarts = [];

    public PeView(byte[] bytes)
    {
        _bytes = bytes;
        int peHeader = BitConverter.ToInt32(_bytes, 0x3c);
        int sectionCount = BitConverter.ToUInt16(_bytes, peHeader + 6);
        int optionalSize = BitConverter.ToUInt16(_bytes, peHeader + 20);
        int optional = peHeader + 24;
        bool pe32Plus = BitConverter.ToUInt16(_bytes, optional) == 0x20b;
        uint exportDirRva = BitConverter.ToUInt32(_bytes, optional + (pe32Plus ? 0x70 : 0x60));
        uint exceptionDirRva = BitConverter.ToUInt32(_bytes, optional + (pe32Plus ? 0x88 : 0x78));
        uint exceptionDirSize = BitConverter.ToUInt32(_bytes, optional + (pe32Plus ? 0x8c : 0x7c));

        _sections = new (uint, uint, uint, uint)[sectionCount];
        int sectionTable = optional + optionalSize;
        for (int i = 0; i < sectionCount; i++)
        {
            int at = sectionTable + i * 40;
            uint virtualSize = BitConverter.ToUInt32(_bytes, at + 8);
            uint rawSize = BitConverter.ToUInt32(_bytes, at + 16);
            _sections[i] = (
                BitConverter.ToUInt32(_bytes, at + 12),
                Math.Max(virtualSize, rawSize),
                BitConverter.ToUInt32(_bytes, at + 20),
                BitConverter.ToUInt32(_bytes, at + 36));
        }

        ReadExports(exportDirRva);
        ReadFunctionStarts(exceptionDirRva, exceptionDirSize);
    }

    public IReadOnlyList<Export> Exports => _exports;

    // The x64 exception directory lists every function, so a resolved routine can be checked
    // against it before a detour lands in the middle of one.
    public bool IsFunctionStart(int rva)
    {
        return Array.BinarySearch(_functionStarts, (uint)rva) >= 0;
    }

    public uint AddressOfFunctionsRva { get; private set; }

    public bool IsExecutable(int rva)
    {
        foreach (var section in _sections)
        {
            if (rva >= section.Va && rva < section.Va + section.Span)
            {
                return (section.Chars & ImageScnMemExecute) != 0;
            }
        }

        return false;
    }

    public byte[] ReadRva(int rva, int count)
    {
        int offset = RvaToOffset(rva);
        if (offset < 0 || offset + count > _bytes.Length)
        {
            return null;
        }

        var buffer = new byte[count];
        Buffer.BlockCopy(_bytes, offset, buffer, 0, count);
        return buffer;
    }

    public bool TryGetExport(string name, out Export export)
    {
        foreach (var candidate in _exports)
        {
            if (candidate.Name == name)
            {
                export = candidate;
                return true;
            }
        }

        export = default;
        return false;
    }

    private int RvaToOffset(int rva)
    {
        foreach (var section in _sections)
        {
            if (rva >= section.Va && rva < section.Va + section.Span)
            {
                return (int)(section.RawPtr + (rva - section.Va));
            }
        }

        return -1;
    }

    private void ReadFunctionStarts(uint dirRva, uint dirSize)
    {
        int dir = dirRva == 0 ? -1 : RvaToOffset((int)dirRva);
        if (dir < 0)
        {
            return;
        }

        int count = (int)(dirSize / 12);
        var starts = new uint[count];
        for (int i = 0; i < count; i++)
        {
            starts[i] = BitConverter.ToUInt32(_bytes, dir + i * 12);
        }

        Array.Sort(starts);
        _functionStarts = starts;
    }

    private void ReadExports(uint exportDirRva)
    {
        if (exportDirRva == 0)
        {
            return;
        }

        int dir = RvaToOffset((int)exportDirRva);
        if (dir < 0)
        {
            return;
        }

        uint nameCount = BitConverter.ToUInt32(_bytes, dir + 0x18);
        AddressOfFunctionsRva = BitConverter.ToUInt32(_bytes, dir + 0x1c);
        int functions = RvaToOffset((int)AddressOfFunctionsRva);
        int names = RvaToOffset(BitConverter.ToInt32(_bytes, dir + 0x20));
        int ordinals = RvaToOffset(BitConverter.ToInt32(_bytes, dir + 0x24));
        if (functions < 0 || names < 0 || ordinals < 0)
        {
            return;
        }

        for (int i = 0; i < nameCount; i++)
        {
            int nameOffset = RvaToOffset(BitConverter.ToInt32(_bytes, names + i * 4));
            if (nameOffset < 0)
            {
                continue;
            }

            int end = nameOffset;
            while (end < _bytes.Length && _bytes[end] != 0)
            {
                end++;
            }

            int ordinal = BitConverter.ToUInt16(_bytes, ordinals + i * 2);
            int rva = BitConverter.ToInt32(_bytes, functions + ordinal * 4);
            _exports.Add(new Export(Encoding.ASCII.GetString(_bytes, nameOffset, end - nameOffset), ordinal, rva));
        }
    }
}
