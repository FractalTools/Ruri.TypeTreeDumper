using System.Text.Json;

namespace Ruri.TypeTreeDumper;

// Source: UTTDumper/include/engine.h. Named EngineOptions (not Options) to avoid
// colliding with System.Text.Json.Serialization.JsonSerializerOptions/
// JsonSerializerContext.Options in the same namespace - the STJ source generator
// resolves a bare "Options" identifier incorrectly when a same-named type is in scope.
internal sealed class EngineOptions
{
    public required string Name { get; init; }
    public uint Delay { get; init; }
    public required string Binary { get; init; }
    public required string OutputDirectory { get; init; }
    public TransferInstruction Transfer { get; init; } = TransferInstruction.SerializeGameRelease;
    public bool JsonDump { get; init; } = true;
    public bool TextDump { get; init; } = true;
    public bool BinaryDump { get; init; } = true;
    public required List<uint> Exclude { get; init; }
    public ulong Version { get; init; }
    public ulong CommonStrings { get; init; }
    public ulong Rtti { get; init; }
    public ulong TypeTreeCtor { get; init; }
    public ulong TypeTree { get; init; }
    public ulong Produce { get; init; }
}

// Source: UTTDumper/lib/engine.cpp. Config format changed from TOML to JSON per explicit
// request - parsed with JsonDocument (a plain DOM/tree reader), which needs no type info
// or reflection at all and is therefore fully AOT/trim-safe by construction, unlike the
// former Tomlyn-based parser (which needed [RequiresUnreferencedCode]/
// [RequiresDynamicCode]-flagged reflection for its generic Deserialize<T> path). Schema
// mirrors the original TOML structure directly: a top-level "engine" object selects, by
// name, a sibling top-level object holding that game's fields - same multi-profile
// mechanic the original config.toml's "[engine]/name" + "[<name>]" pair provided, just
// re-expressed as JSON objects instead of TOML tables. Numeric RVA fields accept either a
// JSON number or a "0x"-prefixed hex string (kept string-friendly since RVAs are always
// read/written in hex in practice); "version" accepts a literal version string, a
// "0x"-prefixed hex string RVA, or a plain JSON number RVA.
internal sealed unsafe class Engine(string dllPath)
{
    private const string ConfigFileName = "config.json";

    private UnityVersion? _version;

    public EngineOptions Options { get; private set; } = null!;
    public UnityVersion Version => _version!.Value;
    public CommonString CommonString { get; private set; } = null!;
    public Rtti Rtti { get; private set; } = null!;
    public TypeTreeGenerator TypeTreeGenerator { get; private set; } = null!;
    public NativeObject NativeObject { get; private set; } = null!;

    // The RVAs actually used this run (whether pinned by config.json or auto-detected) -
    // Initialize() only ever computes these into local variables, so without capturing them
    // here there is no way to recover what was found after the fact.
    public ulong ResolvedCommonStrings { get; private set; }
    public ulong ResolvedRtti { get; private set; }
    public ulong ResolvedTypeTree { get; private set; }
    public ulong ResolvedTypeTreeCtor { get; private set; }
    public ulong ResolvedProduce { get; private set; }

    public void Parse()
    {
        string parent = Path.GetDirectoryName(dllPath) ?? "";
        string configPath = Path.Combine(parent, ConfigFileName);

        Console.WriteLine($"Parsing config file {ConfigFileName}..");

        using JsonDocument document = ParseDocument(configPath);
        JsonElement root = document.RootElement;

        if (!root.TryGetProperty("engine", out JsonElement engineNode))
            throw new InvalidOperationException("no game is selected !!");

        string name = GetString(engineNode, "name") ?? "";
        if (string.IsNullOrEmpty(name))
            throw new InvalidOperationException("no game is selected !!");

        if (!root.TryGetProperty(name, out JsonElement gameNode))
            throw new InvalidOperationException($"no configuration found for key {name}");

        string binary = GetString(gameNode, "binary") ?? "";
        if (string.IsNullOrEmpty(binary))
            throw new InvalidOperationException("binary name is empty !!");

        string outputDir = GetString(gameNode, "output_dir") ?? "";
        if (string.IsNullOrEmpty(outputDir))
            outputDir = Path.Combine(parent, name);

        // exclude must be present (can be an empty array): the source unconditionally
        // dereferences the parsed array, which is UB in C++ if the key is absent. That
        // mechanism isn't portable, so this throws a clear error instead of replicating
        // undefined behavior - a deliberate, minor, and explicitly-noted departure.
        if (!gameNode.TryGetProperty("exclude", out JsonElement excludeEl) || excludeEl.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("exclude array is missing in config.json !!");

        var exclude = new List<uint>();
        foreach (JsonElement item in excludeEl.EnumerateArray())
            exclude.Add((uint)item.GetInt64());

        uint delay = gameNode.TryGetProperty("delay", out JsonElement delayEl) ? (uint)delayEl.GetInt64() : 0;
        var transfer = gameNode.TryGetProperty("transfer", out JsonElement transferEl)
            ? (TransferInstruction)transferEl.GetInt64()
            : TransferInstruction.SerializeGameRelease;

        // json_dump has no default: it is required, matching value<bool>().value() which
        // throws std::bad_optional_access if missing or not a bool.
        if (!gameNode.TryGetProperty("json_dump", out JsonElement jsonDumpEl) ||
            (jsonDumpEl.ValueKind != JsonValueKind.True && jsonDumpEl.ValueKind != JsonValueKind.False))
            throw new InvalidOperationException("json_dump is missing or not a bool in config.json !!");
        bool jsonDump = jsonDumpEl.GetBoolean();

        bool textDump = gameNode.TryGetProperty("text_dump", out JsonElement textDumpEl) && textDumpEl.ValueKind == JsonValueKind.True;
        bool binaryDump = gameNode.TryGetProperty("binary_dump", out JsonElement binaryDumpEl) && binaryDumpEl.ValueKind == JsonValueKind.True;

        ulong commonStrings = GetFlexibleHex(gameNode, "common_strings");
        ulong rtti = GetFlexibleHex(gameNode, "rtti");
        ulong typeTree = GetFlexibleHex(gameNode, "type_tree");
        ulong typeTreeCtor = GetFlexibleHex(gameNode, "type_tree_ctor");
        ulong produce = GetFlexibleHex(gameNode, "produce");

        ulong version = 0;
        if (!gameNode.TryGetProperty("version", out JsonElement versionEl))
        {
            throw new InvalidOperationException($"invalid version for key {name}");
        }
        else if (versionEl.ValueKind == JsonValueKind.String)
        {
            string versionStr = versionEl.GetString()!;
            if (versionStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                version = Convert.ToUInt64(versionStr[2..], 16);
            else
                _version = new UnityVersion(versionStr);
        }
        else if (versionEl.ValueKind == JsonValueKind.Number)
        {
            version = versionEl.GetUInt64();
        }
        else
        {
            throw new InvalidOperationException($"invalid version for key {name}");
        }

        Options = new EngineOptions
        {
            Name = name,
            Delay = delay,
            Binary = binary,
            OutputDirectory = outputDir,
            Transfer = transfer,
            JsonDump = jsonDump,
            TextDump = textDump,
            BinaryDump = binaryDump,
            Exclude = exclude,
            Version = version,
            CommonStrings = commonStrings,
            Rtti = rtti,
            TypeTreeCtor = typeTreeCtor,
            TypeTree = typeTree,
            Produce = produce,
        };
    }

    private static JsonDocument ParseDocument(string configPath)
    {
        try
        {
            return JsonDocument.Parse(File.ReadAllText(configPath));
        }
        catch (Exception err)
        {
            throw new InvalidOperationException($"Error while parsing config file {ConfigFileName}: {err.Message}");
        }
    }

    private static string? GetString(JsonElement node, string propertyName) =>
        node.TryGetProperty(propertyName, out JsonElement el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;

    private static ulong GetFlexibleHex(JsonElement node, string propertyName)
    {
        if (!node.TryGetProperty(propertyName, out JsonElement el))
            return 0;

        if (el.ValueKind == JsonValueKind.Number)
            return el.GetUInt64();

        if (el.ValueKind == JsonValueKind.String)
        {
            string s = el.GetString()!;
            return s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? Convert.ToUInt64(s[2..], 16)
                : ulong.Parse(s);
        }

        return 0;
    }

    // Returns false (not a thrown error) specifically when RTTI initialization fails, to
    // match Engine::initialize's early `return false` there versus the throw used by
    // every other failure path. Source: engine.cpp:180-310.
    public bool Initialize()
    {
        try
        {
            Console.WriteLine("Initializing engine...");

            nint @base = NativeMethods.GetModuleHandleA(Options.Binary);
            List<Section> sections = Scanner.ModuleSections(@base);

            // Version: explicit string (from parse) > GetUnityBuildFullVersion RVA > file-version resource.
            if (_version is null && Options.Version != 0)
            {
                var getUnityVersion = (delegate* unmanaged<nint>)(@base + (nint)Options.Version);
                nint versionStrPtr = getUnityVersion();
                _version = new UnityVersion(RawMemory.ReadCString(versionStrPtr));
            }
            if (_version is null)
            {
                Console.WriteLine("No version configured, detecting from module file version...");

                UnityVersion? detected = Scanner.ModuleVersion(Options.Binary);
                if (detected is null)
                    throw new InvalidOperationException("unable to auto-detect Unity version, set [<game>].version in config.json");

                _version = detected;
            }

            Console.WriteLine($"Unity version: {_version}");

            nint commonStrings;
            if (Options.CommonStrings != 0)
            {
                commonStrings = @base + (nint)Options.CommonStrings;
            }
            else
            {
                Console.WriteLine("No common_strings configured, scanning...");

                commonStrings = Scanner.FindCommonStrings(sections);
                if (commonStrings == 0)
                    throw new InvalidOperationException("unable to locate the common string buffer, set [<game>].common_strings in config.json");

                Console.WriteLine($"Found common strings at RVA 0x{(nuint)(commonStrings - @base):x}");
            }

            CommonString = new CommonString(commonStrings);
            ResolvedCommonStrings = (ulong)(commonStrings - @base);

            nint rtti;
            if (Options.Rtti != 0)
            {
                rtti = @base + (nint)Options.Rtti;
            }
            else
            {
                Console.WriteLine("No rtti configured, scanning for the runtime type array...");

                rtti = Scanner.FindRuntimeTypes(sections, Version);
                if (rtti == 0)
                    throw new InvalidOperationException("unable to locate the RTTI runtime type array, set [<game>].rtti in config.json");

                Console.WriteLine($"Found runtime type array at RVA 0x{(nuint)(rtti - @base):x}");
            }

            Rtti = new Rtti(rtti, Version);
            if (!Rtti.Initialize())
            {
                Console.WriteLine("Failed to initialize RTTI, aborting...");
                return false;
            }
            ResolvedRtti = (ulong)(rtti - @base);

            nint typeTree = Options.TypeTree != 0 ? @base + (nint)Options.TypeTree : 0;
            nint typeTreeCtor = Options.TypeTreeCtor != 0 ? @base + (nint)Options.TypeTreeCtor : 0;

            if (typeTree == 0 || typeTreeCtor == 0)
            {
                Console.WriteLine("Scanning for type tree functions...");

                TypeTreeFunctions fns = Scanner.FindTypeTreeFunctions(sections);
                if (typeTree == 0 && fns.GetTypeTree != 0)
                {
                    typeTree = fns.GetTypeTree;
                    Console.WriteLine($"Found type_tree at RVA 0x{(nuint)(typeTree - @base):x}");
                }
                if (typeTreeCtor == 0 && fns.Ctor != 0)
                {
                    typeTreeCtor = fns.Ctor;
                    Console.WriteLine($"Found type_tree_ctor at RVA 0x{(nuint)(typeTreeCtor - @base):x}");
                }
            }

            if (typeTree == 0)
                throw new InvalidOperationException("type_tree not found (auto-scan failed), set [<game>].type_tree in config.json");
            if (typeTreeCtor == 0 && Version < new UnityVersion(2022, 2, 0, 'f', 0))
                throw new InvalidOperationException("type_tree_ctor not found (auto-scan failed), set [<game>].type_tree_ctor in config.json");

            TypeTreeGenerator = new TypeTreeGenerator(typeTree, typeTreeCtor, CommonString, Version);
            ResolvedTypeTree = (ulong)(typeTree - @base);
            ResolvedTypeTreeCtor = typeTreeCtor != 0 ? (ulong)(typeTreeCtor - @base) : 0;

            nint produce = Options.Produce != 0 ? @base + (nint)Options.Produce : 0;
            if (produce == 0)
            {
                Console.WriteLine("No produce configured, scanning...");

                produce = Scanner.FindProduce(sections, @base);
                if (produce == 0)
                    throw new InvalidOperationException("unable to locate Object::Produce, set [<game>].produce in config.json");

                Console.WriteLine($"Found produce at RVA 0x{(nuint)(produce - @base):x}");
            }

            NativeObject = new NativeObject(produce, Version);
            ResolvedProduce = (ulong)(produce - @base);
        }
        catch (Exception err)
        {
            throw new InvalidOperationException($"Error while initializing engine: {err.Message}", err);
        }

        return true;
    }
}
