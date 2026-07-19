namespace Ruri.TypeTreeDumper;

// RFC 1320 MD4. Source: UTTDumper/include/md4.h. Standard, publicly specified algorithm -
// verified independently against the RFC 1320 Appendix A test vectors (see scratchpad
// Md4SelfCheck), not just transcribed from the reference.
internal sealed class Md4
{
    private const int BlockLength = 64;

    private ulong _count;
    private readonly uint[] _x = new uint[16];
    private readonly uint[] _context = new uint[4];
    private readonly byte[] _buffer = new byte[BlockLength];

    public Md4() => Reset();

    private void Reset()
    {
        _count = 0;
        _context[0] = 0x67452301;
        _context[1] = 0xefcdab89;
        _context[2] = 0x98badcfe;
        _context[3] = 0x10325476;
        Array.Clear(_x);
        Array.Clear(_buffer);
    }

    public void Update(ReadOnlySpan<byte> data)
    {
        int bufferIndex = (int)(_count % BlockLength);
        _count += (ulong)data.Length;
        int partialLength = BlockLength - bufferIndex;
        int i = 0;

        if (data.Length >= partialLength)
        {
            data.Slice(0, partialLength).CopyTo(_buffer.AsSpan(bufferIndex));
            Transform(_buffer);
            i = partialLength;
            while (i + BlockLength - 1 < data.Length)
            {
                Transform(data.Slice(i, BlockLength));
                i += BlockLength;
            }
            bufferIndex = 0;
        }

        if (i < data.Length)
            data.Slice(i).CopyTo(_buffer.AsSpan(bufferIndex));
    }

    public void Update(int value) => Update(BitConverter.GetBytes(value));

    public byte[] Digest()
    {
        int bufferIndex = (int)(_count % BlockLength);
        int paddingLength = bufferIndex < 56 ? 56 - bufferIndex : 120 - bufferIndex;

        var tail = new byte[paddingLength + 8];
        tail[0] = 0x80;

        ulong bitCount = _count * 8;
        for (int i = 0; i < 8; i++)
            tail[paddingLength + i] = (byte)(bitCount >> (8 * i));

        Update(tail);

        var result = new byte[16];
        for (int i = 0; i < 4; i++)
            for (int j = 0; j < 4; j++)
                result[i * 4 + j] = (byte)(_context[i] >> (8 * j));

        Reset();
        return result;
    }

    private void Transform(ReadOnlySpan<byte> block)
    {
        int offset = 0;
        for (int i = 0; i < 16; i++)
        {
            _x[i] = (uint)(block[offset] & 0xff) |
                    (uint)((block[offset + 1] & 0xff) << 8) |
                    (uint)((block[offset + 2] & 0xff) << 16) |
                    (uint)((block[offset + 3] & 0xff) << 24);
            offset += 4;
        }

        uint a = _context[0];
        uint b = _context[1];
        uint c = _context[2];
        uint d = _context[3];

        foreach (int i in stackalloc[] { 0, 4, 8, 12 })
        {
            a = Ff(a, b, c, d, _x[i + 0], 3);
            d = Ff(d, a, b, c, _x[i + 1], 7);
            c = Ff(c, d, a, b, _x[i + 2], 11);
            b = Ff(b, c, d, a, _x[i + 3], 19);
        }

        foreach (int i in stackalloc[] { 0, 1, 2, 3 })
        {
            a = Gg(a, b, c, d, _x[i + 0], 3);
            d = Gg(d, a, b, c, _x[i + 4], 5);
            c = Gg(c, d, a, b, _x[i + 8], 9);
            b = Gg(b, c, d, a, _x[i + 12], 13);
        }

        foreach (int i in stackalloc[] { 0, 2, 1, 3 })
        {
            a = Hh(a, b, c, d, _x[i + 0], 3);
            d = Hh(d, a, b, c, _x[i + 8], 9);
            c = Hh(c, d, a, b, _x[i + 4], 11);
            b = Hh(b, c, d, a, _x[i + 12], 15);
        }

        _context[0] += a;
        _context[1] += b;
        _context[2] += c;
        _context[3] += d;
    }

    private static uint Ff(uint a, uint b, uint c, uint d, uint x, int s) =>
        uint.RotateLeft(a + ((b & c) | (~b & d)) + x, s);

    private static uint Gg(uint a, uint b, uint c, uint d, uint x, int s) =>
        uint.RotateLeft(a + ((b & (c | d)) | (c & d)) + x + 0x5A827999, s);

    private static uint Hh(uint a, uint b, uint c, uint d, uint x, int s) =>
        uint.RotateLeft(a + (b ^ c ^ d) + x + 0x6ED9EBA1, s);
}
