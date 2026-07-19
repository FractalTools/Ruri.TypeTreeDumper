using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Ruri.TypeTreeDumper;

// Source: UTTDumper/lib/windll.cpp, lib/init.cpp.
//
// DllMain is exported directly via [UnmanagedCallersOnly(EntryPoint = "DllMain")],
// matching the original's architecture exactly: a plain LoadLibrary-based injection (or
// DLL search-order hijacking) runs this with no further action needed, same as the
// original. This requires excluding NativeAOT's own dllmain.obj from the link (done in
// Ruri.TypeTreeDumper.csproj's RemoveStubDllMain target) since it unconditionally claims the
// DllMain symbol itself - confirmed via dumpbin /disasm that object is a one-line stub
// (mov eax,1; ret) with no runtime bootstrap, so nothing is lost by removing it.
//
// Residual, documented risk (not fully eliminable, inherent to NativeAOT on Windows, not
// specific to this tool): a .NET runtime team member (jkotas, dotnet/runtime#66546) states
// managed code is not officially supported inside DllMain because the loader lock is held
// and the first managed entry triggers the runtime's own lazy startup, which may call APIs
// unsafe under that lock. The original C++ tool sidesteps the equivalent native concern by
// doing nothing in DllMain except an immediate CreateThread call (one of the few officially
// safe calls inside DllMain) and returning - this port does exactly the same, minimizing
// (though not by official guarantee eliminating) that risk exactly as the reference does
// for its own native equivalent.
internal static unsafe class EntryPoint
{
    private const uint DllProcessAttach = 1;

    [UnmanagedCallersOnly(EntryPoint = "DllMain", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static bool DllMain(nint hModule, uint ulReasonForCall, nint lpReserved)
    {
        if (ulReasonForCall == DllProcessAttach)
        {
            NativeMethods.CreateThread(0, 0, &StartWin, hModule, 0, null);
        }

        return true;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static uint StartWin(nint hModule)
    {
        NativeMethods.AllocConsole();
        // No RedirectIO/ResetIO equivalent needed: unlike std::cout/cin (bound at static
        // init, before AllocConsole runs), System.Console binds to GetStdHandle lazily on
        // first use, which is after AllocConsole here - it picks up the new console
        // directly.

        Span<char> path = stackalloc char[260];
        uint pathLen;
        fixed (char* pathPtr = path)
        {
            pathLen = NativeMethods.GetModuleFileNameW(hModule, pathPtr, (uint)path.Length);
        }

        RunDump(new string(path[..(int)pathLen]));

        Console.WriteLine("Press any key to continue...");
        Console.Read();

        NativeMethods.FreeConsole();

        // DEVIATION (platform limitation, not a port choice): the original calls
        // FreeLibraryAndExitThread to unload itself here. NativeAOT libraries do not
        // support unloading (confirmed via official Microsoft docs: "Unloading Native
        // AOT libraries (via dlclose or FreeLibrary) is not supported"), so this just
        // exits the thread - the DLL stays mapped, idle, for the rest of the process.
        return 0;
    }

    // Source: UTTDumper/lib/init.cpp:9-32.
    private static void RunDump(string path)
    {
        try
        {
            var engine = new Engine(path);
            engine.Parse();

            if (engine.Options.Delay > 0)
            {
                Console.WriteLine($"Waiting for {engine.Options.Delay} seconds...");
                Thread.Sleep(TimeSpan.FromSeconds(engine.Options.Delay));
            }

            if (!engine.Initialize())
            {
                Console.WriteLine("Unable to initialize engine, aborting...");
                return;
            }

            var dumper = new Dumper(engine);
            dumper.Execute();
        }
        catch (Exception e)
        {
            Console.WriteLine("Error while dumping...");
            Console.WriteLine(e.Message);
            Console.WriteLine("Aborting...");
        }
    }
}
