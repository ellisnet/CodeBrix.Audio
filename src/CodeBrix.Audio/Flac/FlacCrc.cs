namespace CodeBrix.Audio.Flac;

/// <summary>
/// The two CRCs a FLAC stream carries: CRC-8 over each frame header and CRC-16 over each
/// complete frame.
/// </summary>
/// <remarks>
/// These are what let a decoder tell a real frame from a byte sequence that merely looks like a
/// sync code, and what turns a truncated or damaged file into a clean error instead of noise.
/// </remarks>
internal static class FlacCrc
{
    private static readonly byte[] Crc8Table = BuildCrc8Table();
    private static readonly ushort[] Crc16Table = BuildCrc16Table();

    /// <summary>Updates a CRC-8 register (polynomial x^8 + x^2 + x + 1) with one byte.</summary>
    public static byte Update8(byte crc, byte value) => Crc8Table[crc ^ value];

    /// <summary>Updates a CRC-16 register (polynomial x^16 + x^15 + x^2 + 1) with one byte.</summary>
    public static ushort Update16(ushort crc, byte value) =>
        (ushort)((crc << 8) ^ Crc16Table[(crc >> 8) ^ value]);

    private static byte[] BuildCrc8Table()
    {
        var table = new byte[256];
        for (var i = 0; i < 256; i++)
        {
            var crc = (byte)i;
            for (var bit = 0; bit < 8; bit++)
                crc = (byte)((crc & 0x80) != 0 ? (crc << 1) ^ 0x07 : crc << 1);
            table[i] = crc;
        }

        return table;
    }

    private static ushort[] BuildCrc16Table()
    {
        var table = new ushort[256];
        for (var i = 0; i < 256; i++)
        {
            var crc = (ushort)(i << 8);
            for (var bit = 0; bit < 8; bit++)
                crc = (ushort)((crc & 0x8000) != 0 ? (crc << 1) ^ 0x8005 : crc << 1);
            table[i] = crc;
        }

        return table;
    }
}
