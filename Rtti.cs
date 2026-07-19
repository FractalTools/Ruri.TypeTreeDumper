namespace Ruri.TypeTreeDumper;

// Source: UTTDumper/include/rtti.h, lib/rtti.cpp. Struct layouts read via explicit raw
// offsets rather than marshaled structs, since these overlay live engine memory whose
// layout is only known through reverse engineering, not a documented ABI - offset reads
// make exactly what is assumed about the target's memory explicit and auditable.
internal interface IRtti
{
    IRtti? Base();
    string Name { get; }
    string NameSpace { get; }
    string Module { get; }
    uint TypeID { get; }
    int Size { get; }
    uint TypeIndex { get; }
    uint DescendantCount { get; }
    bool IsAbstract { get; }
    bool IsSealed { get; }
    bool IsEditorOnly { get; }
    bool IsStripped { get; }
    nint Attributes { get; }
    ulong AttributeCount { get; }
    List<IRtti> Derived { get; }
    nint Ptr { get; }
}

internal static class RttiExtensions
{
    public static string FullName(this IRtti rtti) =>
        string.IsNullOrEmpty(rtti.NameSpace) || string.IsNullOrEmpty(rtti.Name) ? rtti.Name : $"{rtti.NameSpace}.{rtti.Name}";
}

internal static unsafe class RawMemory
{
    public static nint ReadPointer(nint @base, nuint offset) => *(nint*)((byte*)@base + offset);
    public static int ReadInt32(nint @base, nuint offset) => *(int*)((byte*)@base + offset);
    public static uint ReadUInt32(nint @base, nuint offset) => *(uint*)((byte*)@base + offset);
    public static ulong ReadUInt64(nint @base, nuint offset) => *(ulong*)((byte*)@base + offset);
    public static bool ReadBool(nint @base, nuint offset) => *((byte*)@base + offset) != 0;

    public static string ReadCString(nint ptr)
    {
        if (ptr == 0) return "";
        return System.Runtime.InteropServices.Marshal.PtrToStringUTF8(ptr) ?? "";
    }
}

// >= 2017.3: base, factory, className, classNamespace, module, typeID, size, typeIndex,
// descendantCount, isAbstract, isSealed, isEditorOnly, isStripped, attributes, attributeCount.
internal sealed class RttiUnity2017_3(nint ptr) : IRtti
{
    public nint Ptr { get; } = ptr;
    public IRtti? Base() { nint b = RawMemory.ReadPointer(Ptr, 0x00); return b != 0 ? new RttiUnity2017_3(b) : null; }
    public string Name => RawMemory.ReadCString(RawMemory.ReadPointer(Ptr, 0x10));
    public string NameSpace => RawMemory.ReadCString(RawMemory.ReadPointer(Ptr, 0x18));
    public string Module => RawMemory.ReadCString(RawMemory.ReadPointer(Ptr, 0x20));
    public uint TypeID => RawMemory.ReadUInt32(Ptr, 0x28);
    public int Size => RawMemory.ReadInt32(Ptr, 0x2C);
    public uint TypeIndex => RawMemory.ReadUInt32(Ptr, 0x30);
    public uint DescendantCount => RawMemory.ReadUInt32(Ptr, 0x34);
    public bool IsAbstract => RawMemory.ReadBool(Ptr, 0x38);
    public bool IsSealed => RawMemory.ReadBool(Ptr, 0x39);
    public bool IsEditorOnly => RawMemory.ReadBool(Ptr, 0x3A);
    public bool IsStripped => RawMemory.ReadBool(Ptr, 0x3B);
    public nint Attributes => RawMemory.ReadPointer(Ptr, 0x40);
    public ulong AttributeCount => RawMemory.ReadUInt64(Ptr, 0x48);
    public List<IRtti> Derived { get; } = new();
}

// [5.5, 2017.3): base, factory, className, classNamespace, typeID, size, typeIndex,
// descendantCount, isAbstract, isSealed, isEditorOnly. No module/isStripped/attributes.
internal sealed class RttiUnity5_5(nint ptr) : IRtti
{
    public nint Ptr { get; } = ptr;
    public IRtti? Base() { nint b = RawMemory.ReadPointer(Ptr, 0x00); return b != 0 ? new RttiUnity5_5(b) : null; }
    public string Name => RawMemory.ReadCString(RawMemory.ReadPointer(Ptr, 0x10));
    public string NameSpace => RawMemory.ReadCString(RawMemory.ReadPointer(Ptr, 0x18));
    public string Module => "";
    public uint TypeID => RawMemory.ReadUInt32(Ptr, 0x20);
    public int Size => RawMemory.ReadInt32(Ptr, 0x24);
    public uint TypeIndex => RawMemory.ReadUInt32(Ptr, 0x28);
    public uint DescendantCount => RawMemory.ReadUInt32(Ptr, 0x2C);
    public bool IsAbstract => RawMemory.ReadBool(Ptr, 0x30);
    public bool IsSealed => RawMemory.ReadBool(Ptr, 0x31);
    public bool IsEditorOnly => RawMemory.ReadBool(Ptr, 0x32);
    public bool IsStripped => false;
    public nint Attributes => 0;
    public ulong AttributeCount => 0;
    public List<IRtti> Derived { get; } = new();
}

// [5.4, 5.5): base, factory, className, typeID, size, typeIndex, descendantCount,
// isAbstract, isSealed, isEditorOnly. No classNamespace. Not exercised by the target
// (Unity 2021.3.34 always uses the >=2017.3 layout) - ported verbatim from source,
// untested against a live 5.4 binary.
internal sealed class RttiUnity5_4(nint ptr) : IRtti
{
    public nint Ptr { get; } = ptr;
    public IRtti? Base() { nint b = RawMemory.ReadPointer(Ptr, 0x00); return b != 0 ? new RttiUnity5_4(b) : null; }
    public string Name => RawMemory.ReadCString(RawMemory.ReadPointer(Ptr, 0x10));
    public string NameSpace => "";
    public string Module => "";
    public uint TypeID => RawMemory.ReadUInt32(Ptr, 0x18);
    public int Size => RawMemory.ReadInt32(Ptr, 0x1C);
    public uint TypeIndex => RawMemory.ReadUInt32(Ptr, 0x20);
    public uint DescendantCount => RawMemory.ReadUInt32(Ptr, 0x24);
    public bool IsAbstract => RawMemory.ReadBool(Ptr, 0x28);
    public bool IsSealed => RawMemory.ReadBool(Ptr, 0x29);
    public bool IsEditorOnly => RawMemory.ReadBool(Ptr, 0x2A);
    public bool IsStripped => false;
    public nint Attributes => 0;
    public ulong AttributeCount => 0;
    public List<IRtti> Derived { get; } = new();
}

// [5.2, 5.4): base, factory, typeID, className, size, isAbstract. Not exercised by the
// target - ported verbatim from source, untested against a live 5.2 binary.
internal sealed class RttiUnity5_2(nint ptr) : IRtti
{
    public nint Ptr { get; } = ptr;
    public IRtti? Base() { nint b = RawMemory.ReadPointer(Ptr, 0x00); return b != 0 ? new RttiUnity5_2(b) : null; }
    public string Name => RawMemory.ReadCString(RawMemory.ReadPointer(Ptr, 0x18));
    public string NameSpace => "";
    public string Module => "";
    public uint TypeID => RawMemory.ReadUInt32(Ptr, 0x10);
    public int Size => RawMemory.ReadInt32(Ptr, 0x20);
    public uint TypeIndex => 0;
    public uint DescendantCount => 0;
    public bool IsAbstract => RawMemory.ReadBool(Ptr, 0x24);
    public bool IsSealed => false;
    public bool IsEditorOnly => false;
    public bool IsStripped => false;
    public nint Attributes => 0;
    public ulong AttributeCount => 0;
    public List<IRtti> Derived { get; } = new();
}

internal static unsafe class RttiFactory
{
    private static readonly UnityVersion V2017_3 = new(2017, 3, 0, 'f', 0);
    private static readonly UnityVersion V5_5 = new(5, 5, 0, 'f', 0);
    private static readonly UnityVersion V5_4 = new(5, 4, 0, 'f', 0);
    private static readonly UnityVersion V5_2 = new(5, 2, 0, 'f', 0);

    public static IRtti Create(nint ptr, UnityVersion version)
    {
        if (version >= V2017_3) return new RttiUnity2017_3(ptr);
        if (version >= V5_5) return new RttiUnity5_5(ptr);
        if (version >= V5_4) return new RttiUnity5_4(ptr);
        if (version >= V5_2) return new RttiUnity5_2(ptr);
        throw new ArgumentException("unknown version !!");
    }
}

// RTTI::ms_runtimeTypes container. Source: rtti.cpp:178-210 (RTTI::initialize).
internal sealed unsafe class Rtti(nint ptr, UnityVersion version)
{
    private const int MaxRuntimeTypeId = 2000;
    private static readonly UnityVersion V5_5 = new(5, 5, 0, 'f', 0);

    public List<IRtti> Types { get; } = new();

    public bool Initialize()
    {
        if (version >= V5_5)
        {
            int count = RawMemory.ReadInt32(ptr, 0);
            for (int i = 0; i < count; i++)
            {
                nint entryPtr = RawMemory.ReadPointer(ptr, (nuint)(nint.Size + i * nint.Size));
                Types.Add(RttiFactory.Create(entryPtr, version));
            }
        }
        else
        {
            var classIdToRtti = (delegate* unmanaged<int, nint>)ptr;
            for (int i = 0; i < MaxRuntimeTypeId; i++)
            {
                nint entryPtr = classIdToRtti(i);
                Types.Add(RttiFactory.Create(entryPtr, version));
            }
        }

        foreach (IRtti type in Types)
        {
            IRtti? baseType = type.Base();
            if (baseType is null) continue;

            string baseFullName = baseType.FullName();
            foreach (IRtti candidate in Types)
            {
                if (candidate.FullName() == baseFullName)
                {
                    candidate.Derived.Add(type);
                    break;
                }
            }
        }

        return Types.Count != 0;
    }
}
