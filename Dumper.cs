using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ruri.TypeTreeDumper;

[JsonSerializable(typeof(Info))]
[JsonSerializable(typeof(TypeTreeConfig))]
internal partial class DumperJsonContext : JsonSerializerContext
{
}

// Not part of the original C++ tool's output - the RVAs actually used this run (whether
// pinned by config.json or auto-detected) are otherwise only ever visible in console
// output, with no artifact left behind for reuse. Field names/hex-string format match
// config.json's per-profile RVA fields exactly, so this can be pasted directly into a
// config.json profile to get a pinned, no-auto-detect config for this exact binary.
internal sealed class TypeTreeConfig
{
    [JsonPropertyName("version")]
    public required string Version { get; init; }
    [JsonPropertyName("common_strings")]
    public required string CommonStrings { get; init; }
    [JsonPropertyName("rtti")]
    public required string Rtti { get; init; }
    [JsonPropertyName("type_tree")]
    public required string TypeTree { get; init; }
    [JsonPropertyName("type_tree_ctor")]
    public required string TypeTreeCtor { get; init; }
    [JsonPropertyName("produce")]
    public required string Produce { get; init; }
}

// Source: UTTDumper/lib/dumper.cpp, include/dumper.h.
internal sealed class Dumper(Engine engine)
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private Info _info = null!;

    public void Execute()
    {
        Console.WriteLine($"Starting dumper. UnityVersion {engine.Version}");
        Directory.CreateDirectory(engine.Options.OutputDirectory);

        TransferInstruction release = engine.Options.Transfer | TransferInstruction.SerializeGameRelease;
        TransferInstruction editor = engine.Options.Transfer & ~TransferInstruction.SerializeGameRelease;

        _info = new Info(engine, release, editor);

        if (engine.Options.JsonDump)
        {
            DumpClassesJson();
            DumpHashesJson();
            DumpInfoJson();
            DumpTypeTreeConfig();
        }

        if (engine.Options.TextDump)
        {
            DumpRtti();
            DumpStruct(release);
        }

        if (engine.Options.BinaryDump)
        {
            DumpStringData(engine.CommonString);
            DumpStructData(release);
        }

        Console.WriteLine("Success");
    }

    private static List<InfoClass> SortedByTypeId(List<InfoClass> classes) =>
        classes.OrderBy(c => c.TypeID).ToList();

    private void DumpStringData(CommonString commonString)
    {
        Console.WriteLine("Writing common string buffer...");

        using var fs = new FileStream(Path.Combine(engine.Options.OutputDirectory, "strings.dat"), FileMode.Create, FileAccess.Write);
        long length = commonString.End - commonString.Begin - 1;
        unsafe
        {
            var span = new ReadOnlySpan<byte>((void*)commonString.Begin, (int)length);
            fs.Write(span);
        }
    }

    // net8.0's JsonWriterOptions/JsonSerializerOptions have no IndentCharacter/IndentSize/
    // NewLine (added in .NET 9 - unusable here because net10.0's NativeAOT deadlocks on
    // LoadLibrary for a custom-exported DllMain, verified empirically, so this targets
    // net8.0 instead). Utf8JsonWriter's plain Indented=true default is, on this platform,
    // already exactly 2-space indent with CRLF newlines (confirmed via raw byte
    // inspection), which is exactly what classes.json/hashes.json need with zero further
    // configuration. info.json needs tab indent specifically, so it is serialized with the
    // same 2-space/CRLF default and then post-processed, converting each line's leading
    // "2 spaces per level" into "1 tab per level" (safe here because none of this tool's
    // JSON string values ever contain embedded newlines).
    private void DumpInfoJson()
    {
        Console.WriteLine("Writing information json...");

        var options = new JsonSerializerOptions(DumperJsonContext.Default.Options)
        {
            WriteIndented = true,
            // System.Text.Json's default encoder escapes '<', '>', '&', '\'' etc. as \uXXXX
            // for HTML-embedding safety - nlohmann::json's default (what the original tool
            // uses) only escapes what RFC 8259 actually requires, so PPtr<T>-style type
            // names come out raw. UnsafeRelaxedJsonEscaping matches that: it escapes only
            // control characters, '"', and '\\'.
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        string json = JsonSerializer.Serialize(_info, options.GetTypeInfo(typeof(Info)));
        File.WriteAllText(Path.Combine(engine.Options.OutputDirectory, "info.json"), ConvertTwoSpaceIndentToTabs(json), Utf8NoBom);
    }

    private void DumpTypeTreeConfig()
    {
        Console.WriteLine("Writing TypeTreeConfig.json...");

        var config = new TypeTreeConfig
        {
            Version = engine.Version.ToString(),
            CommonStrings = $"0x{engine.ResolvedCommonStrings:x}",
            Rtti = $"0x{engine.ResolvedRtti:x}",
            TypeTree = $"0x{engine.ResolvedTypeTree:x}",
            TypeTreeCtor = $"0x{engine.ResolvedTypeTreeCtor:x}",
            Produce = $"0x{engine.ResolvedProduce:x}",
        };

        var options = new JsonSerializerOptions(DumperJsonContext.Default.Options) { WriteIndented = true };
        string json = JsonSerializer.Serialize(config, options.GetTypeInfo(typeof(TypeTreeConfig)));
        File.WriteAllText(Path.Combine(engine.Options.OutputDirectory, "TypeTreeConfig.json"), json, Utf8NoBom);
    }

    // Single pass over the source string via spans - the prior Split("\r\n") + per-line
    // `new string('\t', n) + line[spaceCount..]` + Join("\r\n") allocated a full string
    // array plus 2-3 heap strings per line (measured as the dominant cost of DumpInfoJson
    // at realistic class counts, ahead of the JSON serialization itself).
    private static string ConvertTwoSpaceIndentToTabs(string json)
    {
        var sb = new StringBuilder(json.Length);
        ReadOnlySpan<char> remaining = json;

        while (true)
        {
            int newlineIndex = remaining.IndexOf("\r\n");
            ReadOnlySpan<char> line = newlineIndex < 0 ? remaining : remaining[..newlineIndex];

            int spaceCount = 0;
            while (spaceCount < line.Length && line[spaceCount] == ' ')
                spaceCount++;

            sb.Append('\t', spaceCount / 2);
            sb.Append(line[spaceCount..]);

            if (newlineIndex < 0) break;
            sb.Append("\r\n");
            remaining = remaining[(newlineIndex + 2)..];
        }

        return sb.ToString();
    }

    private void DumpClassesJson()
    {
        Console.WriteLine("Writing classes.json...");

        using var fs = new FileStream(Path.Combine(engine.Options.OutputDirectory, "classes.json"), FileMode.Create, FileAccess.Write);
        var writerOptions = new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        using (var writer = new Utf8JsonWriter(fs, writerOptions))
        {
            writer.WriteStartObject();
            foreach (InfoClass cls in SortedByTypeId(_info.Classes))
                writer.WriteString(cls.TypeID.ToString(), cls.Name);
            writer.WriteEndObject();
        }
        fs.Write("\r\n"u8);
    }

    private void DumpHashesJson()
    {
        Console.WriteLine("Writing hashes.json...");

        using var fs = new FileStream(Path.Combine(engine.Options.OutputDirectory, "hashes.json"), FileMode.Create, FileAccess.Write);
        var writerOptions = new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        using (var writer = new Utf8JsonWriter(fs, writerOptions))
        {
            writer.WriteStartObject();
            foreach (InfoClass cls in SortedByTypeId(_info.Classes))
            {
                if (!cls.IsAbstract && cls.ReleaseRootNode is not null)
                {
                    var md4 = new Md4();
                    cls.ReleaseRootNode.Hash(md4);
                    byte[] hash = md4.Digest();
                    writer.WriteString(cls.Name, Convert.ToHexString(hash));
                }
            }
            writer.WriteEndObject();
        }
        fs.Write("\r\n"u8);
    }

    private void DumpRtti()
    {
        Console.WriteLine("Writing RTTI...");

        using var fs = new FileStream(Path.Combine(engine.Options.OutputDirectory, "RTTI.dump"), FileMode.Create, FileAccess.Write);
        using var writer = new StreamWriter(fs, Utf8NoBom);

        foreach (InfoClass cls in SortedByTypeId(_info.Classes))
        {
            writer.Write($"PersistentTypeID {cls.TypeID}\r\n");
            writer.Write($"    Name {cls.Name}\r\n");
            writer.Write($"    Namespace {cls.Namespace}\r\n");
            writer.Write($"    Module {cls.Module}\r\n");
            writer.Write($"    Base {cls.Base}\r\n");
            writer.Write($"    DescendantCount {cls.DescendantCount}\r\n");
            writer.Write($"    IsAbstract {(cls.IsAbstract ? "True" : "False")}\r\n");
            writer.Write($"    IsSealed {(cls.IsSealed ? "True" : "False")}\r\n");
            writer.Write($"    IsStripped {(cls.IsStripped ? "True" : "False")}\r\n");
            writer.Write($"    IsEditorOnly {(cls.IsEditorOnly ? "True" : "False")}\r\n");
            writer.Write("\r\n");
        }
    }

    // Source: dumper.cpp:143-199. Walks base chains by NAME through the already-built
    // `classes` list - independent of, and separate from, the RTTI-based walk done during
    // production (Info.cs). The source has no guard against a base name that can't be
    // found in `classes` (find_if miss leaves `iter` unchanged, looping forever) - proven
    // to never occur for this target (7 successful reference dumps across game versions),
    // but a literal infinite loop isn't worth reproducing, so this breaks out instead of
    // hanging if it's ever hit. This is a deliberate, explicitly-noted departure, not a
    // silent fix.
    private void DumpStruct(TransferInstruction transfer)
    {
        Console.WriteLine("Writing structure information dump...");

        List<InfoClass> classes = _info.Classes;
        using var fs = new FileStream(Path.Combine(engine.Options.OutputDirectory, "structs.dump"), FileMode.Create, FileAccess.Write);

        int typeCount = 0;
        foreach (InfoClass type in SortedByTypeId(classes))
        {
            InfoClass iter = type;

            var inheritance = new StringBuilder();
            while (true)
            {
                inheritance.Append(iter.Name);
                if (string.IsNullOrEmpty(iter.Base)) break;
                inheritance.Append(" <- ");

                InfoClass? next = classes.FirstOrDefault(c => c.Name == iter.Base);
                if (next is null) break;
                iter = next;
            }

            fs.Write(Utf8NoBom.GetBytes($"\n// classID{{{type.TypeID}}}: {inheritance}\r\n"));
            iter = type;

            while (iter.IsAbstract)
            {
                fs.Write(Utf8NoBom.GetBytes($"// {iter.Name} is abstract\r\n"));
                if (string.IsNullOrEmpty(iter.Base)) break;

                InfoClass? next = classes.FirstOrDefault(c => c.Name == iter.Base);
                if (next is null) break;
                iter = next;
            }

            InfoNode? tree = (transfer & TransferInstruction.SerializeGameRelease) != 0 ? iter.ReleaseRootNode : iter.EditorRootNode;
            if (tree is not null)
                DumpNodes(tree, fs);

            typeCount++;
        }
    }

    private static void DumpNodes(InfoNode node, Stream stream)
    {
        for (int i = 0; i < node.Level; i++)
            stream.WriteByte((byte)'\t');

        string line = $"{node.TypeName} {node.Name} // ByteSize{{{(uint)node.ByteSize:x}}}, Index{{{FormatSignedHex(node.Index)}}}, Version{{{FormatSignedHex(node.Version)}}}, IsArray{{{node.TypeFlags:x}}}, MetaFlag{{{node.MetaFlag:x}}}\r\n";
        stream.Write(Utf8NoBom.GetBytes(line));

        foreach (InfoNode sub in node.SubNodes)
            DumpNodes(sub, stream);
    }

    // C++ std::format("{:x}", signedValue) formats negative signed integers as a literal
    // '-' followed by the hex magnitude, unlike C#'s "x" format (which always emits the
    // two's-complement bit pattern) - this only matters for values that are actually
    // negative, which real populated tree nodes never are in practice.
    private static string FormatSignedHex(long value) => value < 0 ? $"-{-value:x}" : value.ToString("x");

    // Source: dumper.cpp:201-271. Independent production pass over the raw RTTI list
    // (not m_info->classes) - re-produces every object and re-generates every type tree,
    // duplicating work already done for Info. Ported as-is: this is the reference's own
    // structure, not something introduced here.
    private unsafe void DumpStructData(TransferInstruction transfer)
    {
        Console.WriteLine("Writing structure information...");

        using var fs = new FileStream(Path.Combine(engine.Options.OutputDirectory, "structs.dat"), FileMode.Create, FileAccess.Write);
        using var bw = new BinaryWriter(fs);

        bw.Write(Utf8NoBom.GetBytes(engine.Version.ToString()));
        bw.Write((byte)0);
        bw.Write(7);
        bw.Write((byte)1); // hasTypeTrees

        long countPos = fs.Position;

        List<IRtti> types = engine.Rtti.Types;

        int typeCount = 0;
        bw.Write(typeCount);
        bw.Flush();

        foreach (IRtti type in types.OrderBy(t => t.TypeID))
        {
            Console.WriteLine($"[{typeCount}] Child: {type.NameSpace}::{type.Name}, {type.Module}, {type.TypeID}");
            Console.WriteLine($"[{typeCount}] Getting base type...");

            IRtti iter = type;
            while (iter.IsAbstract)
            {
                IRtti? baseType = iter.Base();
                if (baseType is null) break;
                iter = baseType;
            }

            Console.WriteLine($"[{typeCount}] Base: {iter.NameSpace}::{iter.Name}, {iter.Module}, {iter.TypeID}");

            if (iter.TypeID == 116) continue; // MonoManager

            if (engine.Options.Exclude.Contains(iter.TypeID))
            {
                Console.WriteLine($"[{typeCount}] Type {iter.Name} is excluded, skipping...");
                continue;
            }

            Console.WriteLine($"[{typeCount}] Producing native object...");
            INativeObject? obj = engine.NativeObject.Produce(iter, 0, CreationMode.Default);
            if (obj is null) continue;

            Console.WriteLine($"[{typeCount}] Produced object {obj.InstanceID}. Persistent = {(obj.IsPersistent ? "true" : "false")}.");
            Console.WriteLine($"[{typeCount}] Generating type tree...");

            ITypeTree tree = engine.TypeTreeGenerator.GenerateTypeTree(obj, transfer);

            Console.WriteLine($"[{typeCount}] Getting GUID...");
            bw.Write(iter.TypeID);
            int n = iter.TypeID < 0 ? 0x20 : 0x10;
            for (int j = 0; j < n; j++)
                bw.Write((byte)0);

            DumpBinary(tree, bw);

            typeCount++;
        }

        bw.Flush();
        fs.Position = countPos;
        bw.Write(typeCount);
    }

    private static unsafe void DumpBinary(ITypeTree tree, BinaryWriter writer)
    {
        List<ITypeTreeNode> nodes = tree.Nodes;

        writer.Write((uint)nodes.Count);

        DynamicArrayView stringBuffer = tree.StringBuffer;
        writer.Write((uint)stringBuffer.Size);

        foreach (ITypeTreeNode node in nodes)
        {
            writer.Write(node.Version);
            writer.Write(node.Level);
            writer.Write((byte)node.NodeType);
            writer.Write(node.TypeStrOffset);
            writer.Write(node.NameStrOffset);
            writer.Write(node.ByteSize);
            writer.Write(node.Index);
            writer.Write((uint)node.Meta);
        }

        var bufferSpan = new ReadOnlySpan<byte>((void*)stringBuffer.DataPtr, (int)stringBuffer.Size);
        writer.Write(bufferSpan);
    }
}
