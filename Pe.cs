namespace Ruri.TypeTreeDumper;

// Half-open [Begin, End) of one entry of the module's exception directory. MSVC splits a
// single logical function into several such entries, so a range is a chunk, not necessarily
// a whole function.
internal readonly struct FunctionRange(nint begin, nint end)
{
    public readonly nint Begin = begin;
    public readonly nint End = end;
}

internal static unsafe class Pe
{
    private const int ExceptionDirectoryIndex = 3;
    private const ushort Pe32PlusMagic = 0x20B;
    private const int RuntimeFunctionSize = 12;

    // The x64 exception directory doubles as an index of every function the compiler emitted,
    // which is the only way to delimit functions in a stripped binary without walking code.
    public static List<FunctionRange> FunctionTable(nint @base)
    {
        var ranges = new List<FunctionRange>();
        if (@base == 0)
            return ranges;

        byte* baseAddress = (byte*)@base;
        if (*(ushort*)baseAddress != NativeMethods.ImageDosSignature)
            return ranges;

        byte* ntHeaders = baseAddress + *(int*)(baseAddress + 0x3C);
        if (*(uint*)ntHeaders != NativeMethods.ImageNtSignature)
            return ranges;

        byte* optionalHeader = ntHeaders + 4 + 20;
        if (*(ushort*)optionalHeader != Pe32PlusMagic)
            return ranges;

        uint directoryCount = *(uint*)(optionalHeader + 108);
        if (directoryCount <= ExceptionDirectoryIndex)
            return ranges;

        byte* directory = optionalHeader + 112 + ExceptionDirectoryIndex * 8;
        uint tableRva = *(uint*)directory;
        uint tableSize = *(uint*)(directory + 4);
        if (tableRva == 0 || tableSize < RuntimeFunctionSize)
            return ranges;

        uint sizeOfImage = *(uint*)(optionalHeader + 56);
        uint previousEnd = 0;

        var entry = (uint*)(baseAddress + tableRva);
        for (uint offset = 0; offset + RuntimeFunctionSize <= tableSize; offset += RuntimeFunctionSize, entry += 3)
        {
            if (entry[0] == 0 && entry[1] == 0)
                break;

            // A protected module can have its .pdata contents wiped after load while the
            // data directory that points at it stays intact, which yields a table of the
            // right length full of nonsense. Ascending, non-empty, in-image ranges are what
            // a real table looks like; anything else is not usable as function boundaries.
            if (entry[0] < previousEnd || entry[0] >= entry[1] || entry[1] > sizeOfImage)
                return new List<FunctionRange>();

            previousEnd = entry[0];
            ranges.Add(new FunctionRange(@base + (nint)entry[0], @base + (nint)entry[1]));
        }

        return ranges;
    }
}
