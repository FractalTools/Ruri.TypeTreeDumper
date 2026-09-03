using System.Runtime.InteropServices;

namespace Ruri.TypeTreeDumper;

[StructLayout(LayoutKind.Sequential)]
internal readonly struct MemLabelId(int identifier)
{
    public readonly int Identifier = identifier;

    // MemLabelIdentifier values. The engine carries its own label-name table - an array of
    // const char* indexed by the identifier - so these are not guesses: entry 0x38 reads
    // "BaseObject" and entry 0x53 reads "TypeTree", confirmed by locating the "TypeTree"
    // literal's slot in that table and calibrating the table base so that BaseObject lands
    // on the identifier Object::Produce is known to be called with.
    public static readonly MemLabelId BaseObject = new(0x38);
    public static readonly MemLabelId TypeTree = new(0x53);
}

// Source: UTTDumper/include/native_object.h:9-13.
internal enum CreationMode : int
{
    Default = 0,
    FromNonMainThread = 1,
    DefaultNoLock = 2,
}

// Source: UTTDumper/include/native_object.h:15-25.
[Flags]
internal enum Hide : int
{
    None = 0,
    HideInHierarchy = 1 << 0,
    HideInInspector = 1 << 1,
    DontSaveInEditor = 1 << 2,
    NotEditable = 1 << 3,
    DontSaveInBuild = 1 << 4,
    DontUnloadUnusedAsset = 1 << 5,
    DontSave = 52,
    HideAndDontSave = 61,
}

internal interface INativeObject
{
    int InstanceID { get; }
    MemLabelId MemLabel { get; }
    byte Temporary { get; }
    Hide Hide { get; }
    bool IsPersistent { get; }
    uint CachedTypeIndex { get; }
    nint Ptr { get; }
}

internal static class BitRange
{
    // Faithful port of INativeObject::range<R,L,N> (native_object.h:29-37). Verified by
    // hand-simulating the three bitset shifts: the result leaves bits [R,L) at their
    // original position rather than realigning them to bit 0. None of the call sites
    // below are consumed by the dump pipeline (only .Ptr is; the type-index sentinel
    // check in Info.cs reads the same dword directly with its own >>21, bypassing this
    // interface) - ported verbatim for interface parity, not fixed up.
    public static uint Range(uint bits, int r, int l, int n = 32)
    {
        uint b = bits;
        b >>= r;
        b <<= n - l + r;
        b >>= n - l;
        return b;
    }
}

// Source: UTTDumper/lib/native_object.cpp:5-27.
internal sealed unsafe class NativeObjectUnity5_0 : INativeObject
{
    private readonly nint _ptr;

    public NativeObjectUnity5_0(nint ptr)
    {
        if (ptr == 0) throw new ArgumentException("empty object ptr !!");
        _ptr = ptr;
    }

    private uint Bits => RawMemory.ReadUInt32(_ptr, 0xC);

    public int InstanceID => RawMemory.ReadInt32(_ptr, 0x8);
    public MemLabelId MemLabel => new((int)BitRange.Range(Bits, 0, 11));
    public byte Temporary => (byte)BitRange.Range(Bits, 11, 12);
    public Hide Hide => (Hide)BitRange.Range(Bits, 12, 18);
    public bool IsPersistent => (BitRange.Range(Bits, 18, 19) & 1u) != 0;
    public uint CachedTypeIndex => BitRange.Range(Bits, 19, 29);
    public nint Ptr => _ptr;
}

// Source: UTTDumper/include/native_object.h:48-66, lib/native_object.cpp:29-60.
internal sealed unsafe class NativeObject(nint ptr, UnityVersion version)
{
    private static readonly UnityVersion V3_5 = new(3, 5, 0, 'f', 0);
    private static readonly UnityVersion V5_5 = new(5, 5, 0, 'f', 0);
    private static readonly UnityVersion V2017_2 = new(2017, 2, 0, 'f', 0);
    private static readonly UnityVersion V2023_1_0A2 = new(2023, 1, 0, 'a', 2);
    private static readonly UnityVersion V5_0 = new(5, 0, 0, 'f', 0);

    public INativeObject? Produce(IRtti rtti, int instanceId, CreationMode creationMode)
    {
        if (rtti.IsAbstract)
            return null;

        nint result = 0;
        if (version < V3_5)
        {
            throw new NotSupportedException("version not supported !!");
        }
        else if (version < V5_5)
        {
            var produce = (delegate* unmanaged<uint, int, MemLabelId, CreationMode, nint>)ptr;
            result = produce(rtti.TypeID, instanceId, MemLabelId.BaseObject, creationMode);
        }
        else if (version < V2017_2)
        {
            var produce = (delegate* unmanaged<nint, int, MemLabelId, CreationMode, nint>)ptr;
            result = produce(rtti.Ptr, instanceId, MemLabelId.BaseObject, creationMode);
        }
        else if (version < V2023_1_0A2)
        {
            var produce = (delegate* unmanaged<nint, nint, int, MemLabelId, CreationMode, nint>)ptr;
            result = produce(rtti.Ptr, rtti.Ptr, instanceId, MemLabelId.BaseObject, creationMode);
        }
        // >= 2023.1.0a2: no known signature. Source has no trailing else/throw here either
        // (native_object.cpp:29-51) - result silently stays null, ported as-is.

        return result == 0 ? null : CreateNativeObject(result);
    }

    private INativeObject CreateNativeObject(nint objectPtr)
    {
        if (version < V5_0)
            throw new NotSupportedException("version not supported !!");
        return new NativeObjectUnity5_0(objectPtr);
    }
}
