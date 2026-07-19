namespace Ruri.TypeTreeDumper;

// Source: UTTDumper/include/transfer.h. The source declares this as a plain `enum class`
// (32-bit underlying), but Unity itself widens the real TransferInstruction flags type to
// 64 bits starting at 2021.1 (cross-referenced against DaZombieKiller/TypeTreeRipper,
// source/TypeTree.hpp:26-32). The target here is Unity 2021.3.34, so this is declared as
// 64-bit: every flag value used still fits in the low 32 bits, so this is a strictly safer
// superset of the literal source, not a behavioral change.
[Flags]
internal enum TransferInstruction : long
{
    None = 0,
    ReadWriteFromSerializedFile = 1L << 0,
    AssetMetaDataOnly = 1L << 1,
    HandleDrivenProperties = 1L << 2,
    LoadAndUnloadAssetsDuringBuild = 1L << 3,
    SerializeDebugProperties = 1L << 4,
    IgnoreDebugPropertiesForIndex = 1L << 5,
    BuildPlayerOnlySerializeBuildProperties = 1L << 6,
    IsCloningObject = 1L << 7,
    SerializeGameRelease = 1L << 8,
    SwapEndianess = 1L << 9,
    ResolveStreamedResourceSources = 1L << 10,
    DontReadObjectsFromDiskBeforeWriting = 1L << 11,
    SerializeMonoReload = 1L << 12,
    DontRequireAllMetaFlags = 1L << 13,
    SerializeForPrefabSystem = 1L << 14,
    WarnAboutLeakedObjects = 1L << 15,
    LoadPrefabAsScene = 1L << 16,
    SerializeCopyPasteTransfer = 1L << 17,
    EditorPlayMode = 1L << 18,
    BuildResourceImage = 1L << 19,
    SerializeEditorMinimalScene = 1L << 21,
    GenerateBakedPhysixMeshes = 1L << 22,
    ThreadedSerialization = 1L << 23,
    IsBuiltinResourcesFile = 1L << 24,
    PerformUnloadDependencyTracking = 1L << 25,
    DisableWriteTypeTree = 1L << 26,
    AutoreplaceEditorWindow = 1L << 27,
    DontCreateMonoBehaviourScriptWrapper = 1L << 28,
    SerializeForInspector = 1L << 29,
    SerializedAssetBundleVersion = 1L << 30,
    AllowTextSerialization = 1L << 31,
}

// Source: UTTDumper/include/typetree.h:19-41.
[Flags]
internal enum TransferMeta
{
    None = 0,
    HideInEditor = 1 << 0,
    NotEditable = 1 << 4,
    StrongPPtr = 1 << 6,
    TreatIntegerValueAsBoolean = 1 << 8,
    SimpleEditor = 1 << 11,
    DebugProperty = 1 << 12,
    AlignBytes = 1 << 14,
    AnyChildUsesAlignBytesFlag = 1 << 15,
    IgnoreWithInspectorUndo = 1 << 16,
    EditorDisplaysCharacterMap = 1 << 18,
    IgnoreInMetaFiles = 1 << 19,
    TransferAsArrayEntryNameInMetaFiles = 1 << 20,
    TransferUsingFlowMappingStyle = 1 << 21,
    GenerateBitwiseDifferences = 1 << 22,
    DontAnimate = 1 << 23,
    TransferHex64 = 1 << 24,
    CharPropertyMask = 1 << 25,
    DontValidateUTF8 = 1 << 26,
    FixedBuffer = 1 << 27,
    DisallowSerializedPropertyModification = 1 << 28,
}

// Source: UTTDumper/include/typetree.h:11-17. Named TreeNodeType (not NodeType) to avoid
// a same-name type/property collision with ITypeTreeNode.NodeType, which the C# compiler
// resolves ambiguously in cast expressions.
[Flags]
internal enum TreeNodeType : byte
{
    None = 0,
    IsArray = 1 << 0,
    IsManagedReference = 1 << 1,
    IsManagedReferenceRegistry = 1 << 2,
    IsArrayOfRefs = 1 << 3,
}
