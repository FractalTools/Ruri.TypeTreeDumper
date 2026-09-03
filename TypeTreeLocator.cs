namespace Ruri.TypeTreeDumper;

// Locates TypeTree::TypeTree and TypeTreeCache::GetTypeTree from the shape of the types this
// tool already declares, instead of from where they happen to sit in the call sequence of
// Object::CopySerialized.
//
// The upstream anchor scan - "the code reference to 'Source and Destination Types do not
// match', then the first two distinct direct calls after it" - is a statement about one
// compiler's layout of one function, not about the engine. It is genuinely correct on stock
// builds: replayed against Arknights Endfield 1.3.3 it yields exactly the two RVAs that
// build's verified config pins. But a build is free to restructure that function, and when
// CopySerialized stops constructing a TypeTree inline the same rule silently returns two
// unrelated functions - on the 2025-08 Endfield build it returns a virtual type-equality
// predicate, whose first act is to read a vtable out of the caller-allocated TypeTree and
// call through it, which faults.
//
// What does not move is the type's own shape, which TypeTree2019_3 already spells out:
//
//     TypeTree { TypeTreeShareableData* data; TypeTreeReferencedTypes* refTypes; bool poolOwned; }
//
// so the constructor must null +8 as a qword and +0x10 as a byte through its first argument
// and then allocate the shareable data; and GetTypeTree, which resolves the object's type
// before building the tree, is the caller of that constructor which also reads
// RTTI::ms_runtimeTypes - an address this tool already locates on its own.
//
// x86-64 only, like the rest of the scanner.
internal static unsafe class TypeTreeLocator
{
    private const int MinimumCtorSize = 0x18;
    private const int MaximumCtorSize = 0x140;
    private const int RegisterCount = 16;

    private const byte ModRmDisplacement8 = 0x40;
    private const byte ModRmRegisterDirect = 0xC0;
    private const byte ModRmRipRelative = 0x05;
    private const byte RegisterStackPointer = 4;
    private const byte RegisterBasePointer = 5;
    private const byte RegisterCx = 1;

    // How far a function reaches, without consulting .pdata: MSVC pads the gap to the next
    // function with int3, so a run of them ends the body. Bounded by `maximum` because a
    // function that happens to abut the next one unpadded must not run away.
    private static int CodeExtent(nint begin, int maximum)
    {
        byte* body = (byte*)begin;
        for (int i = 0; i + 3 <= maximum; i++)
        {
            if (body[i] == 0xCC && body[i + 1] == 0xCC && body[i + 2] == 0xCC)
                return i;
        }
        return maximum;
    }

    // A candidate constructor is one where a single base register - the first argument, or a
    // copy of it - receives both of TypeTree's trailing-field nulls, and which then calls out
    // to allocate the shareable data.
    private static bool IsConstructorShaped(nint begin, nint end)
    {
        int size = (int)(end - begin);
        if (size < MinimumCtorSize || size > MaximumCtorSize)
            return false;

        Span<bool> holdsThis = stackalloc bool[RegisterCount];
        Span<bool> nulledReferencedTypes = stackalloc bool[RegisterCount];
        Span<bool> nulledPoolOwned = stackalloc bool[RegisterCount];
        holdsThis.Clear();
        nulledReferencedTypes.Clear();
        nulledPoolOwned.Clear();
        holdsThis[RegisterCx] = true;

        bool calls = false;
        byte* body = (byte*)begin;

        for (int i = 0; i + 4 <= size; i++)
        {
            byte* position = body + i;

            if (*position == 0xE8)
            {
                calls = true;
                continue;
            }

            byte rex = i > 0 ? position[-1] : (byte)0;
            bool hasRex = rex >= 0x40 && rex <= 0x4F;
            int rexB = hasRex ? rex & 1 : 0;
            int rexR = hasRex ? (rex >> 2) & 1 : 0;
            int rexW = hasRex ? (rex >> 3) & 1 : 0;
            byte modrm = position[1];

            // whichever register `this` is copied into also addresses the fields
            if (rexW == 1 && *position == 0x8B && (modrm & ModRmRegisterDirect) == ModRmRegisterDirect && (modrm & 7) == RegisterCx)
                holdsThis[(rexR << 3) | ((modrm >> 3) & 7)] = true;
            if (rexW == 1 && *position == 0x89 && (modrm & ModRmRegisterDirect) == ModRmRegisterDirect && ((modrm >> 3) & 7) == RegisterCx)
                holdsThis[(rexB << 3) | (modrm & 7)] = true;

            if ((modrm & ModRmRegisterDirect) != ModRmDisplacement8)
                continue;

            int rm = modrm & 7;
            if (rm == RegisterStackPointer || rm == RegisterBasePointer)
                continue;

            int baseRegister = (rexB << 3) | rm;
            byte displacement = position[2];
            int opcodeExtension = (modrm >> 3) & 7;

            if (displacement == 0x08 && rexW == 1 &&
                (*position == 0x89 ||
                 (*position == 0xC7 && opcodeExtension == 0) ||
                 (*position == 0x83 && opcodeExtension == 4)))
                nulledReferencedTypes[baseRegister] = true;

            if (displacement == 0x10 && rexW == 0 &&
                (*position == 0x88 ||
                 (*position == 0xC6 && opcodeExtension == 0) ||
                 (*position == 0x80 && opcodeExtension == 4)))
                nulledPoolOwned[baseRegister] = true;
        }

        if (!calls || HasLockPrefix(begin, end))
            return false;

        for (int register = 0; register < RegisterCount; register++)
        {
            if (holdsThis[register] && nulledReferencedTypes[register] && nulledPoolOwned[register])
                return true;
        }

        return false;
    }

    // TypeTree's copy and assignment overloads share the field-nulling shape but adjust the
    // shareable data's reference count atomically. A constructor that has just allocated the
    // block it owns never needs an atomic, so a lock prefix rules a candidate out.
    private static bool HasLockPrefix(nint begin, nint end)
    {
        int size = (int)(end - begin);
        byte* body = (byte*)begin;

        for (int i = 0; i + 2 <= size; i++)
        {
            if (body[i] != 0xF0)
                continue;
            byte following = body[i + 1];
            if ((following >= 0x40 && following <= 0x4F) ||
                following is 0x0F or 0xFF or 0x83 or 0x81 or 0x01 or 0x29 or 0x11 or 0x19)
                return true;
        }

        return false;
    }

    // Instruction lengths are unknown without a full decoder, so every plausible split of
    // opcode bytes before a rip-relative ModRM is tried and the one that resolves to
    // `address` is accepted.
    private static bool ReadsAddress(nint begin, nint end, nint address)
    {
        int size = (int)(end - begin);
        byte* body = (byte*)begin;

        for (int i = 0; i < size; i++)
        {
            byte* position = body + i;
            for (int opcodeLength = 1; opcodeLength <= 3 && i + opcodeLength + 5 <= size; opcodeLength++)
            {
                if ((position[opcodeLength] & 0xC7) != ModRmRipRelative)
                    continue;
                if ((nint)position + opcodeLength + 5 + *(int*)(position + opcodeLength + 1) == address)
                    return true;
            }
        }

        return false;
    }

    private static void CollectCallTargets(nint begin, nint end, HashSet<nint> into)
    {
        int size = (int)(end - begin);
        byte* body = (byte*)begin;
        for (int i = 0; i + 5 <= size; i++)
        {
            if (body[i] != 0xE8)
                continue;
            into.Add((nint)(body + i) + 5 + *(int*)(body + i + 1));
        }
    }

    // Object::CompareTypeTrees builds a TypeTree for each of the two objects and asks
    // GetTypeTree to fill it, passing one distinctive TransferInstructionFlags value:
    //
    //     call    <type_tree_ctor>
    //     call    <type_tree_ctor>
    //     movabs  rdx, SerializeForPrefabSystem | <64-bit-only flag>
    //     call    <type_tree>
    //
    // The low dword of that value is stable across every version checked; the high dword is
    // one of the flags Unity added when it widened TransferInstructionFlags to 64 bits at
    // 2021.1, and which bit it is varies by version, so only its single-bit-ness is relied
    // on. Requiring the preceding call to independently pass the constructor shape test is
    // what makes the anchor unambiguous.
    private static List<(nint Ctor, nint Get)> LocateByComparisonFlags(List<Section> sections, nint @base)
    {
        var found = new List<(nint, nint)>();
        int literals = 0, withCalls = 0, shaped = 0;

        foreach (Section section in sections)
        {
            if (!section.Execute || section.Size < 10)
                continue;

            byte* body = (byte*)section.Begin;
            for (nuint i = 2; i + 8 <= section.Size; i++)
            {
                if (body[i - 2] < 0x48 || body[i - 2] > 0x4F || body[i - 1] < 0xB8 || body[i - 1] > 0xBF)
                    continue;

                ulong immediate = *(ulong*)(body + i);
                uint high = (uint)(immediate >> 32);
                if ((uint)immediate != (uint)TransferInstruction.SerializeForPrefabSystem || high == 0 || (high & (high - 1)) != 0)
                    continue;

                literals++;
                nint site = (nint)(body + i);
                nint get = NearestCall(site, forward: true);
                nint ctor = NearestCall(site, forward: false);
                if (get == 0 || ctor == 0)
                    continue;
                if (!Scanner.IsValidPointer(sections, get, 1, true, false, true) ||
                    !Scanner.IsValidPointer(sections, ctor, 1, true, false, true))
                    continue;

                withCalls++;
                if (!IsConstructorShaped(ctor, ctor + CodeExtent(ctor, MaximumCtorSize)))
                    continue;

                shaped++;
                found.Add((ctor, get));
            }
        }

        Console.WriteLine($"  type_tree: flag probe saw {literals} flag literals, {withCalls} with calls both sides, {shaped} constructor-shaped");
        return found.Distinct().ToList();
    }

    // Nearest direct call either side of the flag literal. The constructor call sits a few
    // instructions before it and GetTypeTree immediately after, so a short window is enough
    // and no function boundary is needed.
    private static nint NearestCall(nint site, bool forward)
    {
        const int Window = 0x40;

        for (int step = 0; step <= Window; step++)
        {
            nint p = forward ? site + step : site - 1 - step;
            if (*(byte*)p != 0xE8)
                continue;
            if (!forward && p + 5 > site)
                continue;
            return p + 5 + *(int*)(p + 1);
        }

        return 0;
    }

    public static TypeTreeFunctions Locate(List<Section> sections, nint @base, nint runtimeTypes)
    {
        var result = new TypeTreeFunctions();

        List<(nint Ctor, nint Get)> byFlags = LocateByComparisonFlags(sections, @base);

        // The shape probe needs to enumerate functions, so it only runs when the module
        // still carries a usable exception directory.
        List<FunctionRange> ranges = Pe.FunctionTable(@base);
        if (ranges.Count == 0)
        {
            Console.WriteLine("  type_tree: no usable exception directory, shape probe skipped");
            return Combine(new List<(nint, nint)>(), byFlags, @base, ref result);
        }

        var indexOf = new Dictionary<nint, int>(ranges.Count);
        for (int i = 0; i < ranges.Count; i++)
            indexOf[ranges[i].Begin] = i;

        var callersOf = new Dictionary<nint, HashSet<nint>>();
        var targets = new HashSet<nint>();
        foreach (FunctionRange range in ranges)
        {
            targets.Clear();
            CollectCallTargets(range.Begin, range.End, targets);
            foreach (nint target in targets)
            {
                if (!Scanner.IsValidPointer(sections, target, 1, true, false, true))
                    continue;
                if (!callersOf.TryGetValue(target, out HashSet<nint>? sites))
                    callersOf[target] = sites = new HashSet<nint>();
                sites.Add(range.Begin);
            }
        }

        // A range nothing ever calls into is a continuation chunk of the range before it.
        nint EntryOf(nint chunk)
        {
            int index = indexOf[chunk];
            while (index > 0 && !callersOf.ContainsKey(ranges[index].Begin))
                index--;
            return ranges[index].Begin;
        }

        var pairs = new HashSet<(nint Ctor, nint Get)>();
        foreach (FunctionRange range in ranges)
        {
            if (!IsConstructorShaped(range.Begin, range.End))
                continue;
            if (!callersOf.TryGetValue(range.Begin, out HashSet<nint>? callers))
                continue;

            foreach (nint caller in callers)
            {
                nint entry = EntryOf(caller);
                if (entry == range.Begin)
                    continue;

                FunctionRange entryRange = ranges[indexOf[entry]];
                if (ReadsAddress(entryRange.Begin, entryRange.End, runtimeTypes))
                    pairs.Add((range.Begin, entry));
            }
        }

        // A function's body is its entry chunk plus every continuation chunk that follows,
        // and a forwarding call can sit in either.
        void CollectWholeBody(nint entry, HashSet<nint> into)
        {
            int index = indexOf[entry];
            for (int i = index; i < ranges.Count && (i == index || !callersOf.ContainsKey(ranges[i].Begin)); i++)
                CollectCallTargets(ranges[i].Begin, ranges[i].End, into);
        }

        // A thin public wrapper that forwards to the real entry point satisfies every
        // criterion the entry point does; keep only what nothing else delegates to.
        var candidates = new HashSet<nint>(pairs.Select(pair => pair.Get));
        var forwarders = new HashSet<nint>();
        foreach (nint candidate in candidates)
        {
            targets.Clear();
            CollectWholeBody(candidate, targets);
            if (targets.Any(target => target != candidate && candidates.Contains(target)))
                forwarders.Add(candidate);
        }

        List<(nint Ctor, nint Get)> byShape = pairs.Where(pair => !forwarders.Contains(pair.Get)).ToList();

        return Combine(byShape, byFlags, @base, ref result);
    }

    private static TypeTreeFunctions Combine(List<(nint Ctor, nint Get)> byShape,
                                             List<(nint Ctor, nint Get)> byFlags,
                                             nint @base,
                                             ref TypeTreeFunctions result)
    {

        // Two independent derivations. Neither fires on every build - the comparison path is
        // sometimes compiled without the 64-bit flag literal, and a build can drop the
        // GetTypeTree-calls-the-constructor edge the shape probe keys on - but where both
        // resolve they have to name the same pair, and disagreement means neither is trusted.
        bool haveShape = byShape.Count == 1;
        bool haveFlags = byFlags.Count == 1;

        if (haveShape && haveFlags && byShape[0] != byFlags[0])
        {
            Console.WriteLine("  type_tree: the two probes disagree, refusing to guess");
            Console.WriteLine($"      by shape: ctor 0x{(nuint)(byShape[0].Ctor - @base):x} type_tree 0x{(nuint)(byShape[0].Get - @base):x}");
            Console.WriteLine($"      by flags: ctor 0x{(nuint)(byFlags[0].Ctor - @base):x} type_tree 0x{(nuint)(byFlags[0].Get - @base):x}");
            return result;
        }

        if (haveShape || haveFlags)
        {
            (nint ctor, nint get) = haveShape ? byShape[0] : byFlags[0];
            Console.WriteLine($"  type_tree: resolved by {(haveShape && haveFlags ? "both probes" : haveShape ? "constructor shape" : "comparison flags")}");
            result.Ctor = ctor;
            result.GetTypeTree = get;
            return result;
        }

        Console.WriteLine($"  type_tree: unresolved (shape probe {byShape.Count} pairs, flag probe {byFlags.Count} pairs)");
        foreach ((nint ctor, nint get) in byShape.Concat(byFlags))
            Console.WriteLine($"      candidate ctor 0x{(nuint)(ctor - @base):x}  type_tree 0x{(nuint)(get - @base):x}");

        return result;
    }
}
