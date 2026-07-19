using System.Text;

namespace Ruri.TypeTreeDumper;

// Source: UTTDumper/include/common_string.h, lib/common_string.cpp.
internal sealed unsafe class CommonString
{
    private readonly nint _begin;
    private readonly nint _end;
    // std::map is always key-ordered; SortedDictionary matches that (a plain Dictionary's
    // enumeration order is unspecified and must not be relied on for info.json's Strings
    // array, which is emitted in ascending offset order).
    private readonly SortedDictionary<nuint, string> _strings = new();

    public CommonString(nint ptr)
    {
        _begin = ptr;
        nuint offset = 0;
        int count = 0;

        while (true)
        {
            byte* p = (byte*)ptr + offset;
            int length = 0;
            while (p[length] != 0) length++;
            if (length == 0) break;

            _strings[offset] = Encoding.UTF8.GetString(p, length);
            offset += (nuint)length + 1;
            count++;
        }

        _end = ptr + (nint)offset;
        Console.WriteLine($"Found {count} common strings.");
    }

    public nint Begin => _begin;
    public nint End => _end;

    public string String(nuint offset) => _strings.TryGetValue(offset, out var s) ? s : "";

    public IReadOnlyDictionary<nuint, string> Strings => _strings;
}
