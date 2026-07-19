namespace Ruri.TypeTreeDumper;

// Source: UTTDumper/include/version.h, lib/version.cpp.
internal readonly struct UnityVersion : IEquatable<UnityVersion>, IComparable<UnityVersion>
{
    public readonly int Major;
    public readonly int Minor;
    public readonly int Patch;
    public readonly char Type;
    public readonly int Build;

    public UnityVersion(int major, int minor, int patch, char type, int build)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        Type = type;
        Build = build;
    }

    public UnityVersion(string text)
    {
        int i = 0;
        Major = ReadInt(text, ref i);
        i++; // '.'
        Minor = ReadInt(text, ref i);
        i++; // '.'
        Patch = ReadInt(text, ref i);
        Type = text[i];
        i++;
        Build = ReadInt(text, ref i);
    }

    private static int ReadInt(string text, ref int i)
    {
        while (i < text.Length && char.IsWhiteSpace(text[i]))
            i++;
        int start = i;
        while (i < text.Length && char.IsAsciiDigit(text[i]))
            i++;
        return int.Parse(text.AsSpan(start, i - start));
    }

    public bool Equals(UnityVersion other) =>
        Major == other.Major && Minor == other.Minor && Patch == other.Patch && Type == other.Type && Build == other.Build;

    public override bool Equals(object? obj) => obj is UnityVersion other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, Type, Build);

    // Faithful port of operator<=> including its bug: when major/minor/patch/type all
    // match it falls through to comparing major against itself again instead of build,
    // so build is never actually significant to ordering. Source: version.h:28-42.
    public int CompareTo(UnityVersion other)
    {
        if (Major != other.Major) return Major.CompareTo(other.Major);
        if (Minor != other.Minor) return Minor.CompareTo(other.Minor);
        if (Patch != other.Patch) return Patch.CompareTo(other.Patch);
        if (Type != other.Type) return Type.CompareTo(other.Type);
        return Major.CompareTo(other.Major);
    }

    public static bool operator ==(UnityVersion left, UnityVersion right) => left.Equals(right);
    public static bool operator !=(UnityVersion left, UnityVersion right) => !left.Equals(right);
    public static bool operator <(UnityVersion left, UnityVersion right) => left.CompareTo(right) < 0;
    public static bool operator <=(UnityVersion left, UnityVersion right) => left.CompareTo(right) <= 0;
    public static bool operator >(UnityVersion left, UnityVersion right) => left.CompareTo(right) > 0;
    public static bool operator >=(UnityVersion left, UnityVersion right) => left.CompareTo(right) >= 0;

    public override string ToString() => $"{Major}.{Minor}.{Patch}{Type}{Build}";
}
