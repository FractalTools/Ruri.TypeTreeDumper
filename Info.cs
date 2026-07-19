using System.Text.Json.Serialization;

namespace Ruri.TypeTreeDumper;

// Source: UTTDumper/include/info.h, lib/info.cpp. Property names are chosen to match the
// JSON key names emitted by utils.h's to_json overloads directly (System.Text.Json
// preserves declaration order and uses the property name as-is with no naming policy
// set), so no [JsonPropertyName] attributes are needed.
internal sealed class InfoString
{
    // Source C++ field is size_t (info.h:12); System.Text.Json has no built-in converter
    // for nint/nuint, so this is ulong rather than nuint - same unsigned 64-bit value
    // domain as size_t, still serializes as a plain JSON number under "Index".
    public required ulong Index { get; init; }
    public required string String { get; init; }

    public static List<InfoString> MakeList(CommonString commonString) =>
        commonString.Strings.Select(kv => new InfoString { Index = kv.Key, String = kv.Value }).ToList();
}

internal sealed class InfoNode
{
    public string TypeName { get; }
    public string Name { get; }
    public byte Level { get; }
    public int ByteSize { get; }
    public int Index { get; }
    public short Version { get; }
    public byte TypeFlags { get; }
    public uint MetaFlag { get; }
    public List<InfoNode> SubNodes { get; } = new();

    // parent is walked during tree construction (RootNode below) but is never one of the
    // fields utils.h's to_json(InfoNode) assigns, so it must not round-trip into JSON here
    // either - left as a plain public property it would also create a reference cycle
    // (child.Parent -> parent.SubNodes -> child -> ...) that the serializer rejects.
    [JsonIgnore]
    public InfoNode? Parent { get; }

    public InfoNode(ITypeTreeNode node, InfoNode? parent = null)
    {
        Level = node.Level;
        TypeName = node.Type;
        Name = node.Name;
        ByteSize = node.ByteSize;
        Index = node.Index;
        Version = node.Version;
        TypeFlags = (byte)node.NodeType;
        MetaFlag = (uint)node.Meta;
        Parent = parent;
    }

    public void Hash(Md4 md4)
    {
        md4.Update(System.Text.Encoding.UTF8.GetBytes(TypeName));
        md4.Update(System.Text.Encoding.UTF8.GetBytes(Name));
        md4.Update(ByteSize);
        md4.Update((int)TypeFlags);
        md4.Update((int)Version);
        md4.Update((int)(MetaFlag & 0x4000));

        foreach (InfoNode sub in SubNodes)
            sub.Hash(md4);
    }

    public static InfoNode RootNode(ITypeTree typeTree)
    {
        List<ITypeTreeNode> nodes = typeTree.Nodes;
        var root = new InfoNode(nodes[0]);

        InfoNode iter = root;
        for (int i = 1; i < nodes.Count; i++)
        {
            ITypeTreeNode treeNode = nodes[i];

            while (treeNode.Level <= iter.Level)
                iter = iter.Parent!;

            var node = new InfoNode(treeNode, iter);
            iter.SubNodes.Add(node);
            iter = node;
        }

        return root;
    }
}

internal sealed class InfoClass
{
    public string Name { get; }
    public string Namespace { get; }
    public string FullName { get; }
    public string Module { get; }
    public uint TypeID { get; }
    public string Base { get; }
    public List<string> Derived { get; }
    public uint DescendantCount { get; }
    public int Size { get; }
    public uint TypeIndex { get; }
    public bool IsAbstract { get; }
    public bool IsSealed { get; }
    public bool IsEditorOnly { get; }
    public bool IsStripped { get; }
    public InfoNode? EditorRootNode { get; set; }
    public InfoNode? ReleaseRootNode { get; set; }

    public InfoClass(IRtti rtti)
    {
        Name = rtti.Name;
        Namespace = rtti.NameSpace;
        FullName = rtti.FullName();
        Module = rtti.Module;
        TypeID = rtti.TypeID;
        IRtti? baseRtti = rtti.Base();
        Base = baseRtti?.Name ?? "";
        DescendantCount = rtti.DescendantCount;
        Size = rtti.Size;
        TypeIndex = rtti.TypeIndex;
        IsAbstract = rtti.IsAbstract;
        IsSealed = rtti.IsSealed;
        IsEditorOnly = rtti.IsEditorOnly;
        IsStripped = rtti.IsStripped;
        Derived = rtti.Derived.Select(d => d?.Name ?? "").ToList();
    }

    // Source: info.cpp:83-137.
    public static List<InfoClass> MakeList(Engine engine, TransferInstruction release, TransferInstruction editor)
    {
        var result = new List<InfoClass>();
        List<IRtti> types = engine.Rtti.Types;

        IEnumerable<int> sortedIndices = Enumerable.Range(0, types.Count).OrderBy(i => types[i].TypeID);

        foreach (int i in sortedIndices)
        {
            IRtti type = types[i];
            var infoClass = new InfoClass(type);

            IRtti iter = type;
            while (iter.IsAbstract)
            {
                IRtti? baseType = iter.Base();
                if (baseType is null) break;
                iter = baseType;
            }

            if (iter.TypeID == 116) continue; // MonoManager

            if (engine.Options.Exclude.Contains(iter.TypeID))
            {
                Console.WriteLine($"Type {iter.Name} is excluded, skipping...");
                continue;
            }

            INativeObject? obj = engine.NativeObject.Produce(iter, 0, CreationMode.Default);

            if (obj is not null)
            {
                // Some custom engines (e.g. a forked 2021.3 ECS build) hand back a produced
                // object whose runtime type index is the unregistered sentinel (high 11
                // bits of object+0xC == 0x7FF/2047). GetTypeTree immediately does
                // ms_runtimeTypes[typeIndex], which on such objects indexes far past the
                // registered-type table and dereferences unrelated data -> access
                // violation. Validate the index before asking for the tree.
                uint objectTypeIndex = RawMemory.ReadUInt32(obj.Ptr, 0xC) >> 21;

                if (objectTypeIndex < (uint)types.Count)
                {
                    ITypeTree releaseTree = engine.TypeTreeGenerator.GenerateTypeTree(obj, release);
                    ITypeTree editorTree = engine.TypeTreeGenerator.GenerateTypeTree(obj, editor);

                    infoClass.ReleaseRootNode = InfoNode.RootNode(releaseTree);
                    infoClass.EditorRootNode = InfoNode.RootNode(editorTree);
                }
                else
                {
                    Console.WriteLine($"Type {iter.Name} produced an object with unregistered type index {objectTypeIndex} (>= {types.Count}), skipping type tree...");
                }
            }

            result.Add(infoClass);
        }

        return result;
    }
}

internal sealed class Info
{
    public string Version { get; }
    public List<InfoString> Strings { get; }
    public List<InfoClass> Classes { get; }

    public Info(Engine engine, TransferInstruction release, TransferInstruction editor)
    {
        Version = engine.Version.ToString();
        Strings = InfoString.MakeList(engine.CommonString);
        Classes = InfoClass.MakeList(engine, release, editor);
    }
}
