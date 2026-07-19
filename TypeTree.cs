using System.Runtime.InteropServices;

namespace Ruri.TypeTreeDumper;

// A raw memory view of a Unity-internal DynamicArray<T>: { T* data; MemLabelId label;
// size_t size; size_t capacity; }. label sits between data and size (4 bytes padded to
// 8), which is easy to miss - offsets here are cross-validated against an independent
// implementation (DaZombieKiller/TypeTreeRipper, source/dynamic_array.hpp:31-35).
internal readonly unsafe struct DynamicArrayView(nint structAddr)
{
    public nint DataPtr => RawMemory.ReadPointer(structAddr, 0);
    public long Size => *(long*)(structAddr + 16);
}

internal interface ITypeTreeNode
{
    short Version { get; }
    byte Level { get; }
    TreeNodeType NodeType { get; }
    uint TypeStrOffset { get; }
    uint NameStrOffset { get; }
    string Type { get; }
    string Name { get; }
    int ByteSize { get; }
    int Index { get; }
    TransferMeta Meta { get; }
}

internal interface ITypeTree
{
    List<ITypeTreeNode> Nodes { get; }
    DynamicArrayView StringBuffer { get; }
    nint Ptr { get; }
    void Populate();
}

// Raw engine node layout used <2019.1 (TypeTreeNode_Unity5_0::Node, typetree.cpp:42-53) -
// 24 bytes: version(2) level(1) type(1) typeStrOffset(4) nameStrOffset(4) byteSize(4)
// index(4) meta(4).
internal readonly unsafe struct RawNode5_0
{
    public readonly short Version;
    public readonly byte Level;
    public readonly TreeNodeType Type;
    public readonly uint TypeStrOffset;
    public readonly uint NameStrOffset;
    public readonly int ByteSize;
    public readonly int Index;
    public readonly TransferMeta Meta;

    public RawNode5_0(nint ptr)
    {
        Version = *(short*)ptr;
        Level = *(byte*)(ptr + 2);
        Type = (TreeNodeType)(*(byte*)(ptr + 3));
        TypeStrOffset = *(uint*)(ptr + 4);
        NameStrOffset = *(uint*)(ptr + 8);
        ByteSize = *(int*)(ptr + 12);
        Index = *(int*)(ptr + 16);
        Meta = (TransferMeta)(*(int*)(ptr + 20));
    }
}

// Raw engine node layout used >=2019.1 (TypeTreeNode_Unity2019_1::Node, typetree.cpp:7-17)
// - 32 bytes: RawNode5_0's 24 bytes plus a trailing refTypeHash(8).
internal readonly unsafe struct RawNode2019_1
{
    public readonly short Version;
    public readonly byte Level;
    public readonly TreeNodeType Type;
    public readonly uint TypeStrOffset;
    public readonly uint NameStrOffset;
    public readonly int ByteSize;
    public readonly int Index;
    public readonly TransferMeta Meta;
    public readonly ulong RefTypeHash;

    public RawNode2019_1(nint ptr)
    {
        Version = *(short*)ptr;
        Level = *(byte*)(ptr + 2);
        Type = (TreeNodeType)(*(byte*)(ptr + 3));
        TypeStrOffset = *(uint*)(ptr + 4);
        NameStrOffset = *(uint*)(ptr + 8);
        ByteSize = *(int*)(ptr + 12);
        Index = *(int*)(ptr + 16);
        Meta = (TransferMeta)(*(int*)(ptr + 20));
        RefTypeHash = *(ulong*)(ptr + 24);
    }
}

internal sealed class TypeTreeNodeUnity5_0(RawNode5_0 node, TypeTreeBase owner) : ITypeTreeNode
{
    public short Version => node.Version;
    public byte Level => node.Level;
    public TreeNodeType NodeType => node.Type;
    public uint TypeStrOffset => node.TypeStrOffset;
    public uint NameStrOffset => node.NameStrOffset;
    public string Type => owner.String(node.TypeStrOffset);
    public string Name => owner.String(node.NameStrOffset);
    public int ByteSize => node.ByteSize;
    public int Index => node.Index;
    public TransferMeta Meta => node.Meta;
}

internal sealed class TypeTreeNodeUnity2019_1(RawNode2019_1 node, TypeTreeBase owner) : ITypeTreeNode
{
    public short Version => node.Version;
    public byte Level => node.Level;
    public TreeNodeType NodeType => node.Type;
    public uint TypeStrOffset => node.TypeStrOffset;
    public uint NameStrOffset => node.NameStrOffset;
    public string Type => owner.String(node.TypeStrOffset);
    public string Name => owner.String(node.NameStrOffset);
    public int ByteSize => node.ByteSize;
    public int Index => node.Index;
    public TransferMeta Meta => node.Meta;
}

// Source: UTTDumper/include/typetree.h:66-88 (ITypeTree).
internal abstract class TypeTreeBase(CommonString commonString) : ITypeTree
{
    protected static readonly MemLabelId CtorMemLabel = new(0x53);

    public List<ITypeTreeNode> Nodes { get; protected set; } = new();
    public abstract nint Ptr { get; }
    public abstract DynamicArrayView StringBuffer { get; }
    public abstract void Populate();

    public string String(uint offset)
    {
        if ((offset & 0x80000000u) != 0)
            return commonString.String((nuint)(offset & 0x7FFFFFFFu));

        return RawMemory.ReadCString(StringBuffer.DataPtr + (nint)offset);
    }
}

// [5.0, 5.3): flat TypeTree { nodes(32); stringBuffer(32); offsets(32); } = 96 bytes.
// Source: typetree.cpp:239-271.
internal sealed unsafe class TypeTree5_0 : TypeTreeBase
{
    private readonly nint _tree;

    public TypeTree5_0(nint ctorPtr, CommonString commonString) : base(commonString)
    {
        _tree = (nint)NativeMemory.AllocZeroed(96);
        var ctor = (delegate* unmanaged<nint, void>)ctorPtr;
        ctor(_tree);
    }

    public override nint Ptr => _tree;
    public override DynamicArrayView StringBuffer => new(_tree + 32);

    public override void Populate()
    {
        var nodesArray = new DynamicArrayView(_tree + 0);
        var nodes = new List<ITypeTreeNode>((int)nodesArray.Size);
        for (long i = 0; i < nodesArray.Size; i++)
            nodes.Add(new TypeTreeNodeUnity5_0(new RawNode5_0(nodesArray.DataPtr + (nint)(i * 24)), this));
        Nodes = nodes;
    }
}

// [5.3, 2019.1): same flat shape as 5.0, 2-arg ctor(tree*, MemLabel).
// Source: typetree.cpp:205-237.
internal sealed unsafe class TypeTree5_3 : TypeTreeBase
{
    private readonly nint _tree;

    public TypeTree5_3(nint ctorPtr, CommonString commonString) : base(commonString)
    {
        _tree = (nint)NativeMemory.AllocZeroed(96);
        var ctor = (delegate* unmanaged<nint, MemLabelId, void>)ctorPtr;
        ctor(_tree, CtorMemLabel);
    }

    public override nint Ptr => _tree;
    public override DynamicArrayView StringBuffer => new(_tree + 32);

    public override void Populate()
    {
        var nodesArray = new DynamicArrayView(_tree + 0);
        var nodes = new List<ITypeTreeNode>((int)nodesArray.Size);
        for (long i = 0; i < nodesArray.Size; i++)
            nodes.Add(new TypeTreeNodeUnity5_0(new RawNode5_0(nodesArray.DataPtr + (nint)(i * 24)), this));
        Nodes = nodes;
    }
}

// [2019.1, 2019.3): TypeTree { data*(8); privateData(inline, 112) } = 120 bytes, 3-arg
// ctor(tree*, MemLabel, 0). Reads go through the `data` pointer, exactly as the source
// does (m_tree.data->stringBuffer) rather than the inline privateData directly.
// Source: typetree.cpp:163-203.
internal sealed unsafe class TypeTree2019_1 : TypeTreeBase
{
    private readonly nint _tree;

    public TypeTree2019_1(nint ctorPtr, CommonString commonString) : base(commonString)
    {
        _tree = (nint)NativeMemory.AllocZeroed(120);
        var ctor = (delegate* unmanaged<nint, MemLabelId, byte, void>)ctorPtr;
        ctor(_tree, CtorMemLabel, 0);
    }

    private nint Data => RawMemory.ReadPointer(_tree, 0);
    public override nint Ptr => _tree;
    public override DynamicArrayView StringBuffer => new(Data + 32);

    public override void Populate()
    {
        var nodesArray = new DynamicArrayView(Data + 0);
        var nodes = new List<ITypeTreeNode>((int)nodesArray.Size);
        for (long i = 0; i < nodesArray.Size; i++)
            nodes.Add(new TypeTreeNodeUnity2019_1(new RawNode2019_1(nodesArray.DataPtr + (nint)(i * 32)), this));
        Nodes = nodes;
    }
}

// [2019.3, 2022.2): TypeTree { data*(8); referencedTypes*(8); poolOwned(1, padded) } = 24
// bytes, 2-arg ctor(tree*, MemLabel). Source: typetree.cpp:120-161.
internal sealed unsafe class TypeTree2019_3 : TypeTreeBase
{
    private readonly nint _tree;

    public TypeTree2019_3(nint ctorPtr, CommonString commonString) : base(commonString)
    {
        _tree = (nint)NativeMemory.AllocZeroed(24);
        var ctor = (delegate* unmanaged<nint, MemLabelId, void>)ctorPtr;
        ctor(_tree, CtorMemLabel);
    }

    private nint Data => RawMemory.ReadPointer(_tree, 0);
    public override nint Ptr => _tree;
    public override DynamicArrayView StringBuffer => new(Data + 32);

    public override void Populate()
    {
        var nodesArray = new DynamicArrayView(Data + 0);
        var nodes = new List<ITypeTreeNode>((int)nodesArray.Size);
        for (long i = 0; i < nodesArray.Size; i++)
            nodes.Add(new TypeTreeNodeUnity2019_1(new RawNode2019_1(nodesArray.DataPtr + (nint)(i * 32)), this));
        Nodes = nodes;
    }
}

// [2022.2, 2023.1.0a2): same 24-byte wrapper shape as 2019.3, but no ctor call at all -
// GetTypeTree allocates the shareable data itself. Its TypeTreeShareableData additionally
// has `levels`/`nextIndex` arrays before stringBuffer (nodes(32)+levels(32)+nextIndex(32)
// = offset 96). Source: typetree.cpp:78-118. This is the wrapper the actual target
// (Unity 2021.3.34) never reaches (that falls in the 2019.3-2022.2 bracket above) but is
// ported for completeness.
internal sealed unsafe class TypeTree2022_2 : TypeTreeBase
{
    private readonly nint _tree;

    public TypeTree2022_2(CommonString commonString) : base(commonString)
    {
        _tree = (nint)NativeMemory.AllocZeroed(24);
    }

    private nint Data => RawMemory.ReadPointer(_tree, 0);
    public override nint Ptr => _tree;
    public override DynamicArrayView StringBuffer => new(Data + 96);

    public override void Populate()
    {
        var nodesArray = new DynamicArrayView(Data + 0);
        var nodes = new List<ITypeTreeNode>((int)nodesArray.Size);
        for (long i = 0; i < nodesArray.Size; i++)
            nodes.Add(new TypeTreeNodeUnity2019_1(new RawNode2019_1(nodesArray.DataPtr + (nint)(i * 32)), this));
        Nodes = nodes;
    }
}

// Source: UTTDumper/include/typetree.h:90-113, lib/typetree.cpp:273-312.
internal sealed unsafe class TypeTreeGenerator(nint getTypeTreePtr, nint ctorPtr, CommonString commonString, UnityVersion version)
{
    private static readonly UnityVersion V5_0 = new(5, 0, 0, 'f', 0);
    private static readonly UnityVersion V5_3 = new(5, 3, 0, 'f', 0);
    private static readonly UnityVersion V2019_1 = new(2019, 1, 0, 'f', 0);
    private static readonly UnityVersion V2019_3 = new(2019, 3, 0, 'f', 0);
    private static readonly UnityVersion V2022_2 = new(2022, 2, 0, 'f', 0);
    private static readonly UnityVersion V2023_1_0A2 = new(2023, 1, 0, 'a', 2);
    private static readonly UnityVersion V2019 = new(2019, 0, 0, 'f', 0);

    private ITypeTree CreateWrapper()
    {
        if (version < V5_0) throw new NotSupportedException("version not supported !!");
        if (version < V5_3) return new TypeTree5_0(ctorPtr, commonString);
        if (version < V2019_1) return new TypeTree5_3(ctorPtr, commonString);
        if (version < V2019_3) return new TypeTree2019_1(ctorPtr, commonString);
        if (version < V2022_2) return new TypeTree2019_3(ctorPtr, commonString);
        if (version < V2023_1_0A2) return new TypeTree2022_2(commonString);
        throw new ArgumentException("unknown version !!");
    }

    // Note the differing argument order/threshold between the two call conventions: this
    // is a coarser ">=2019" (any 2019.x) check, separate from the finer-grained wrapper
    // selection thresholds above.
    public ITypeTree GenerateTypeTree(INativeObject obj, TransferInstruction flags)
    {
        ITypeTree tree = CreateWrapper();

        if (version >= V2019)
        {
            var getTypeTree = (delegate* unmanaged<nint, TransferInstruction, nint, byte>)getTypeTreePtr;
            getTypeTree(obj.Ptr, flags, tree.Ptr);
        }
        else
        {
            var generateTypeTree = (delegate* unmanaged<nint, nint, TransferInstruction, void>)getTypeTreePtr;
            generateTypeTree(obj.Ptr, tree.Ptr, flags);
        }

        tree.Populate();
        return tree;
    }
}
