namespace DRPGServer.Network.Protocol;

/// <summary>
/// CRC16-XMODEM (poly 0x1021, init 0) sobre o payload seguido de 14 bytes zero.
/// Confere com 99% dos pacotes capturados. O cliente às vezes manda CRC errado e o
/// servidor oficial não valida, então só calculamos na saída.
/// </summary>
public static class Crc16
{
    private const int TrailingZeros = PacketHeader.Length;
    private static readonly ushort[] Table = BuildTable();

    public static ushort Compute(ReadOnlySpan<byte> payload)
    {
        ushort crc = 0;
        foreach (byte b in payload)
            crc = (ushort)((crc << 8) ^ Table[(byte)((crc >> 8) ^ b)]);
        for (int i = 0; i < TrailingZeros; i++)
            crc = (ushort)((crc << 8) ^ Table[(byte)(crc >> 8)]);
        return crc;
    }

    private static ushort[] BuildTable()
    {
        var table = new ushort[256];
        for (int i = 0; i < 256; i++)
        {
            ushort value = (ushort)(i << 8);
            for (int bit = 0; bit < 8; bit++)
                value = (value & 0x8000) != 0 ? (ushort)((value << 1) ^ 0x1021) : (ushort)(value << 1);
            table[i] = value;
        }
        return table;
    }
}
