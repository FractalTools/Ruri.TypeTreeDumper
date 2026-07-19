using System.Runtime.InteropServices;

namespace Ruri.TypeTreeDumper;

[StructLayout(LayoutKind.Explicit, Size = 40)]
internal struct ImageSectionHeader
{
    [FieldOffset(8)] public uint VirtualSize;
    [FieldOffset(12)] public uint VirtualAddress;
    [FieldOffset(36)] public uint Characteristics;

    public const uint MemDiscardable = 0x02000000;
    public const uint MemExecute = 0x20000000;
    public const uint MemRead = 0x40000000;
    public const uint MemWrite = 0x80000000;
}

[StructLayout(LayoutKind.Explicit, Size = 20)]
internal struct ImageFileHeader
{
    [FieldOffset(2)] public ushort NumberOfSections;
    [FieldOffset(16)] public ushort SizeOfOptionalHeader;
}

internal static unsafe class NativeMethods
{
    public const ushort ImageDosSignature = 0x5A4D;
    public const uint ImageNtSignature = 0x00004550;

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    public static extern nint GetModuleHandleA(string? lpModuleName);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern uint GetModuleFileNameW(nint hModule, char* lpFilename, uint nSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool AllocConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool FreeConsole();

    // LPTHREAD_START_ROUTINE is WINAPI (__stdcall).
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern nint CreateThread(nint lpThreadAttributes, nuint dwStackSize, delegate* unmanaged[Stdcall]<nint, uint> lpStartAddress, nint lpParameter, uint dwCreationFlags, nint* lpThreadId);

    [DllImport("version.dll", SetLastError = true)]
    public static extern uint GetFileVersionInfoSizeW(char* lptstrFilename, uint* lpdwHandle);

    [DllImport("version.dll", SetLastError = true)]
    public static extern bool GetFileVersionInfoW(char* lptstrFilename, uint dwHandle, uint dwLen, void* lpData);

    // CharSet must be explicit here: DllImport defaults to CharSet.Ansi when unspecified,
    // which would marshal lpSubBlock ("\\") as 1-byte-per-char ANSI instead of the 2-byte
    // UTF-16 the "W" (wide) API actually reads, causing VerQueryValueW to silently fail on
    // a garbled subblock path - confirmed as the root cause of a real, empirically-observed
    // "unable to auto-detect Unity version" failure when actually run.
    [DllImport("version.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool VerQueryValueW(void* pBlock, string lpSubBlock, void** lplpBuffer, uint* puLen);

    // VS_FIXEDFILEINFO, read via VerQueryValue("\\"). Only the two version DWORDs are needed.
    [StructLayout(LayoutKind.Explicit, Size = 52)]
    public struct VsFixedFileInfo
    {
        [FieldOffset(0)] public uint dwSignature;
        [FieldOffset(8)] public uint dwFileVersionMS;
        [FieldOffset(12)] public uint dwFileVersionLS;
    }

    public const uint VsFfiSignature = 0xFEEF04BD;
}
