namespace CommunicationGame.Server.Uart;

/// <summary>
/// Consistent Overhead Byte Stuffing (COBS) encoder/decoder.
/// Encodes data so that 0x00 never appears in the payload,
/// allowing 0x00 to be used as a packet delimiter.
/// </summary>
public static class CobsCodec
{
    public static byte[] Encode(byte[] data)
    {
        if (data.Length == 0)
            return new byte[] { 0x01 };

        var output = new List<byte>();
        int codeIndex = 0;
        byte code = 1;

        output.Add(0);

        for (int i = 0; i < data.Length; i++)
        {
            if (data[i] == 0x00)
            {
                output[codeIndex] = code;
                code = 1;
                codeIndex = output.Count;
                output.Add(0);
            }
            else
            {
                output.Add(data[i]);
                code++;

                if (code == 0xFF)
                {
                    output[codeIndex] = code;
                    code = 1;
                    codeIndex = output.Count;
                    output.Add(0);
                }
            }
        }

        output[codeIndex] = code;
        return output.ToArray();
    }

    public static byte[]? Decode(byte[] encoded)
    {
        if (encoded.Length == 0)
            return null;

        var output = new List<byte>();
        int i = 0;

        while (i < encoded.Length)
        {
            byte code = encoded[i];
            if (code == 0)
                return null;

            i++;

            for (int j = 1; j < code; j++)
            {
                if (i >= encoded.Length)
                    return null;
                output.Add(encoded[i]);
                i++;
            }

            if (code < 0xFF && i < encoded.Length)
            {
                output.Add(0x00);
            }
        }

        if (output.Count > 0 && output[^1] == 0x00)
            output.RemoveAt(output.Count - 1);

        return output.ToArray();
    }
}
