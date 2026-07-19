using System.Runtime.InteropServices;

namespace Ruri.TypeTreeDumper;

// A loaded, non-discardable section of the target module.
internal readonly struct Section
{
    public readonly nint Begin;
    public readonly nuint Size;
    public readonly bool Read;
    public readonly bool Write;
    public readonly bool Execute;

    public Section(nint begin, nuint size, bool read, bool write, bool execute)
    {
        Begin = begin;
        Size = size;
        Read = read;
        Write = write;
        Execute = execute;
    }
}

internal struct TypeTreeFunctions
{
    public nint Ctor;
    public nint GetTypeTree;
}

// Source: UTTDumper/lib/scanner.cpp, include/scanner.h. Windows/x64 only, matching the
// original (auto-detection requires config.toml RVAs on every other platform).
internal static unsafe class Scanner
{
    private static readonly byte[] CommonStringsPattern = "AABB\0AnimationClip\0"u8.ToArray();

    public static List<Section> ModuleSections(nint @base)
    {
        var sections = new List<Section>();
        if (@base == 0)
            return sections;

        byte* baseAddr = (byte*)@base;
        if (*(ushort*)baseAddr != NativeMethods.ImageDosSignature)
            return sections;

        int e_lfanew = *(int*)(baseAddr + 0x3C);
        byte* ntHeaders = baseAddr + e_lfanew;
        if (*(uint*)ntHeaders != NativeMethods.ImageNtSignature)
            return sections;

        ImageFileHeader* fileHeader = (ImageFileHeader*)(ntHeaders + 4);
        byte* sectionTable = ntHeaders + 4 + 20 + fileHeader->SizeOfOptionalHeader;

        var section = (ImageSectionHeader*)sectionTable;
        for (ushort i = 0; i < fileHeader->NumberOfSections; i++, section++)
        {
            if ((section->Characteristics & ImageSectionHeader.MemDiscardable) != 0)
                continue;

            sections.Add(new Section(
                begin: @base + (nint)section->VirtualAddress,
                size: section->VirtualSize,
                read: (section->Characteristics & ImageSectionHeader.MemRead) != 0,
                write: (section->Characteristics & ImageSectionHeader.MemWrite) != 0,
                execute: (section->Characteristics & ImageSectionHeader.MemExecute) != 0));
        }

        return sections;
    }

    public static UnityVersion? ModuleVersion(string binary)
    {
        nint module = NativeMethods.GetModuleHandleA(binary);
        if (module == 0)
            return null;

        Span<char> path = stackalloc char[260];
        uint pathLen;
        fixed (char* pathPtr = path)
        {
            pathLen = NativeMethods.GetModuleFileNameW(module, pathPtr, (uint)path.Length);
        }
        if (pathLen == 0)
            return null;

        fixed (char* pathPtr = path)
        {
            uint handle = 0;
            uint size = NativeMethods.GetFileVersionInfoSizeW(pathPtr, &handle);
            if (size == 0)
                return null;

            byte[] data = new byte[size];
            fixed (byte* dataPtr = data)
            {
                if (!NativeMethods.GetFileVersionInfoW(pathPtr, handle, size, dataPtr))
                    return null;

                void* infoPtr = null;
                uint infoSize = 0;
                if (!NativeMethods.VerQueryValueW(dataPtr, "\\", &infoPtr, &infoSize) || infoPtr == null)
                    return null;

                var info = (NativeMethods.VsFixedFileInfo*)infoPtr;
                if (info->dwSignature != NativeMethods.VsFfiSignature)
                    return null;

                // UnityPlayer.dll encodes the engine version as major.minor.patch.build.
                // build is the internal changeset and is not used for revision dispatch.
                int major = (int)(info->dwFileVersionMS >> 16);
                int minor = (int)(info->dwFileVersionMS & 0xFFFF);
                int patch = (int)(info->dwFileVersionLS >> 16);
                int build = (int)(info->dwFileVersionLS & 0xFFFF);

                return new UnityVersion(major, minor, patch, 'f', build);
            }
        }
    }

    public static bool IsValidPointer(List<Section> sections, nint ptr, nuint size, bool read = true, bool write = false, bool execute = false)
    {
        nuint address = (nuint)ptr;

        foreach (Section section in sections)
        {
            if (read && !section.Read) continue;
            if (write && !section.Write) continue;
            if (execute && !section.Execute) continue;

            nuint begin = (nuint)section.Begin;
            if (address < begin) continue;

            // Overflow-safe form of: address + size <= begin + section.size
            nuint offset = address - begin;
            if (offset <= section.Size && section.Size - offset >= size)
                return true;
        }

        return false;
    }

    // The common string buffer always begins with these two entries.
    public static nint FindCommonStrings(List<Section> sections)
    {
        int patternSize = CommonStringsPattern.Length;

        foreach (Section section in sections)
        {
            if (!section.Read || section.Size < (nuint)patternSize)
                continue;

            var span = new ReadOnlySpan<byte>((void*)section.Begin, (int)section.Size);
            int index = span.IndexOf(CommonStringsPattern);
            if (index >= 0)
                return section.Begin + index;
        }

        return 0;
    }

    private static nint ReadPointer(nint @base, nuint offset) => *(nint*)((byte*)@base + offset);

    private static int ReadInt32(nint @base, nuint offset) => *(int*)((byte*)@base + offset);

    private static bool StringEquals(List<Section> sections, nint ptr, ReadOnlySpan<byte> value)
    {
        if (ptr == 0 || !IsValidPointer(sections, ptr, (nuint)value.Length + 1))
            return false;

        var candidate = new ReadOnlySpan<byte>((void*)ptr, value.Length + 1);
        if (candidate[value.Length] != 0)
            return false;

        return candidate.Slice(0, value.Length).SequenceEqual(value);
    }

    private static bool InExecutable(List<Section> sections, nint addr)
    {
        foreach (Section section in sections)
        {
            if (!section.Execute) continue;
            if ((nuint)addr >= (nuint)section.Begin && (nuint)addr < (nuint)section.Begin + section.Size)
                return true;
        }
        return false;
    }

    // Find the lowest-address occurrence of `value` (with its NUL terminator) in any
    // readable section. `value` must already include the trailing NUL byte.
    private static nint FindString(List<Section> sections, ReadOnlySpan<byte> value)
    {
        foreach (Section section in sections)
        {
            if (!section.Read || section.Size < (nuint)value.Length) continue;

            var span = new ReadOnlySpan<byte>((void*)section.Begin, (int)section.Size);
            int index = span.IndexOf(value);
            if (index >= 0)
                return section.Begin + index;
        }

        return 0;
    }

    // Find the lowest-address RIP-relative `lea r64, [rip+disp32]` in executable memory
    // that resolves to `target`.
    private static nint FindLeaTo(List<Section> sections, nint target)
    {
        foreach (Section section in sections)
        {
            if (!section.Execute || !section.Read || section.Size < 7) continue;

            byte* begin = (byte*)section.Begin;
            byte* sectionEnd = begin + section.Size;
            for (byte* p = begin; p + 7 <= sectionEnd; p++)
            {
                if ((p[0] & 0xF8) == 0x48 && p[1] == 0x8D && (p[2] & 0xC7) == 0x05)
                {
                    nint resolved = (nint)p + 7 + ReadInt32((nint)p, 3);
                    if (resolved == target)
                        return (nint)p;
                }
            }
        }
        return 0;
    }

    // The RTTI field layout is identical across every revision >= 5.5 except that 2017.3
    // inserts a `module` pointer before persistentTypeID (see Rtti.cs). Offsets are
    // expressed in pointer-widths so this stays correct if ever built for x86.
    private static nuint RttiTypeIdOffset(UnityVersion version)
    {
        nuint pointers = version >= new UnityVersion(2017, 3, 0, 'f', 0) ? 5u : 4u;
        return pointers * (nuint)nint.Size;
    }

    private static bool IsRuntimeTypeArray(List<Section> sections, nint candidate, UnityVersion version)
    {
        // Layout: { int32_t count; /* pad to pointer */ RTTI* types[count]; }
        const nuint baseOffset = 0;
        nuint factoryOffset = (nuint)nint.Size;
        nuint classNameOffset = 2 * (nuint)nint.Size;
        nuint typeIdOffset = RttiTypeIdOffset(version);
        nuint rttiReadSize = typeIdOffset + sizeof(int);

        int count = ReadInt32(candidate, 0);
        if (count < 2 || count > 0x4000)
            return false;

        nint typesBase = candidate + nint.Size;

        Span<nint> types = stackalloc nint[2];
        for (int i = 0; i < 2; i++)
        {
            types[i] = ReadPointer(typesBase, (nuint)(i * nint.Size));
            if (!IsValidPointer(sections, types[i], rttiReadSize))
                return false;

            nint factory = ReadPointer(types[i], factoryOffset);
            if (factory != 0 && !IsValidPointer(sections, factory, 1))
                return false;
        }

        // types[0] must be the root Object type: persistentTypeID 0, named "Object", and
        // the base of the next type in the table.
        if (ReadInt32(types[0], typeIdOffset) != 0)
            return false;

        if (ReadPointer(types[1], baseOffset) != types[0])
            return false;

        return StringEquals(sections, ReadPointer(types[0], classNameOffset), "Object"u8);
    }

    // Only >= 5.5 exposes the type table as a { count, types[] } array; older engines
    // resolve types through a function, so scanning does not apply.
    public static nint FindRuntimeTypes(List<Section> sections, UnityVersion version)
    {
        if (version < new UnityVersion(5, 5, 0, 'f', 0))
            return 0;

        // count (+ padding) followed by at least the two entries we validate.
        nuint minimumSpan = 3 * (nuint)nint.Size;

        foreach (Section section in sections)
        {
            // The array is populated at runtime, so it lives in readable+writable data.
            if (!section.Read || !section.Write || section.Size < minimumSpan)
                continue;

            for (nuint offset = 0; offset + minimumSpan <= section.Size; offset += (nuint)nint.Size)
            {
                nint candidate = section.Begin + (nint)offset;
                if (IsRuntimeTypeArray(sections, candidate, version))
                    return candidate;
            }
        }

        return 0;
    }

    // Locate the TypeTree ctor + GetTypeTree by anchoring on the code reference to the
    // "Source and Destination Types do not match" string, then taking the first two
    // distinct direct-call targets after it (skipping the error logger, which is the
    // call immediately followed by a jmp). x86-64 only.
    public static TypeTreeFunctions FindTypeTreeFunctions(List<Section> sections)
    {
        var result = new TypeTreeFunctions();

        nint stringPtr = FindString(sections, "Source and Destination Types do not match\0"u8);
        if (stringPtr == 0) return result;

        nint lea = FindLeaTo(sections, stringPtr);
        if (lea == 0) return result;

        byte* end = (byte*)lea + 0x400;

        nint ctor = 0;
        for (byte* pos = (byte*)lea; pos + 6 <= end;)
        {
            if (*pos != 0xE8)
            {
                pos++;
                continue;
            }

            nint target = (nint)pos + 5 + ReadInt32((nint)pos, 1);
            byte next = pos[5];
            pos += 5;

            if (next == 0xE9 || next == 0xEB) continue; // logger / tail jump
            if (!InExecutable(sections, target)) continue; // stray 0xE8 byte

            if (ctor == 0)
            {
                ctor = target;
            }
            else if (target != ctor)
            {
                result.Ctor = ctor;
                result.GetTypeTree = target;
                return result;
            }
        }

        result.Ctor = ctor; // only the ctor resolved (unexpected)
        return result;
    }

    // Locate Object::Produce by anchoring on the code reference to "Failure to create
    // component of type '%s' (0x%08X)" (the error path taken when producing a component
    // fails), then scanning backward for the nearest preceding direct call. Deliberately
    // avoids function-boundary metadata (.pdata): on protected/packed binaries that can
    // be stale or bogus even when the surrounding code bytes are intact.
    public static nint FindProduce(List<Section> sections, nint @base)
    {
        nint stringPtr = FindString(sections, "Failure to create component of type '%s' (0x%08X)\0"u8);
        if (stringPtr == 0)
        {
            Console.WriteLine("  produce: anchor string not found");
            return 0;
        }
        Console.WriteLine($"  produce: anchor string at RVA 0x{(nuint)(stringPtr - @base):x}");

        nint lea = FindLeaTo(sections, stringPtr);
        if (lea == 0)
        {
            Console.WriteLine("  produce: no lea references the anchor string");
            return 0;
        }
        Console.WriteLine($"  produce: lea reference at RVA 0x{(nuint)(lea - @base):x}");

        const int window = 512;
        nint sectionBegin = lea;
        foreach (Section section in sections)
        {
            if (lea >= section.Begin && lea < section.Begin + (nint)section.Size)
            {
                sectionBegin = section.Begin;
                break;
            }
        }
        nint scanLimit = (lea - sectionBegin) > window ? lea - window : sectionBegin;

        for (byte* pos = (byte*)lea - 1; pos >= (byte*)scanLimit; pos--)
        {
            if (*pos != 0xE8) continue;

            nint target = (nint)pos + 5 + ReadInt32((nint)pos, 1);
            if (InExecutable(sections, target))
                return target;
        }

        Console.WriteLine("  produce: no call instruction found before the anchor");
        return 0;
    }
}
