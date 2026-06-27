namespace CommunicationGame.Server.Uart;

/// <summary>
/// CRC-8 calculator using polynomial 0x07 (x^8 + x^2 + x + 1).
/// Used to validate UART packet integrity.
/// </summary>
public static class Crc8
{
    private static readonly byte[] Table = new byte[256];

    static Crc8()
    {
        const byte polynomial = 0x07;
        for (int i = 0; i < 256; i++)
        {
            byte crc = (byte)i;
            for (int bit = 0; bit < 8; bit++)
            {
                if ((crc & 0x80) != 0)
                    crc = (byte)((crc << 1) ^ polynomial);
                else
                    crc = (byte)(crc << 1);
            }
            Table[i] = crc;
        }
    }

    public static byte Compute(byte[] data, int offset, int length)
    {
        byte crc = 0x00;
        for (int i = offset; i < offset + length; i++)
        {
            crc = Table[crc ^ data[i]];
        }
        return crc;
    }

    public static byte Compute(byte[] data)
    {
        return Compute(data, 0, data.Length);
    }
}
