using CommunicationGame.Shared.Enums;

namespace CommunicationGame.Server.Uart;

/// <summary>
/// Represents a UART binary packet:
/// [TYPE(1)] [SEQ(1)] [LEN(1)] [PAYLOAD(0..N)] [CRC8(1)]
/// The entire packet is COBS-encoded before transmission, with 0x00 delimiter.
/// </summary>
public class UartPacket
{
    public UartPacketType Type { get; set; }
    public byte Sequence { get; set; }
    public byte[] Payload { get; set; } = Array.Empty<byte>();

    public byte[] ToBytes()
    {
        int len = Payload.Length;
        var raw = new byte[3 + len + 1];
        raw[0] = (byte)Type;
        raw[1] = Sequence;
        raw[2] = (byte)len;
        if (len > 0)
            Array.Copy(Payload, 0, raw, 3, len);
        raw[^1] = Crc8.Compute(raw, 0, raw.Length - 1);
        return raw;
    }

    public byte[] Encode()
    {
        var raw = ToBytes();
        var cobsEncoded = CobsCodec.Encode(raw);
        var frame = new byte[cobsEncoded.Length + 1];
        Array.Copy(cobsEncoded, frame, cobsEncoded.Length);
        frame[^1] = 0x00;
        return frame;
    }

    public static UartPacket? Decode(byte[] cobsFrame)
    {
        var raw = CobsCodec.Decode(cobsFrame);
        if (raw == null || raw.Length < 4)
            return null;

        byte expectedCrc = Crc8.Compute(raw, 0, raw.Length - 1);
        if (raw[^1] != expectedCrc)
            return null;

        byte type = raw[0];
        byte seq = raw[1];
        byte len = raw[2];

        if (raw.Length != 3 + len + 1)
            return null;

        var payload = new byte[len];
        if (len > 0)
            Array.Copy(raw, 3, payload, 0, len);

        if (!Enum.IsDefined(typeof(UartPacketType), type))
            return null;

        return new UartPacket
        {
            Type = (UartPacketType)type,
            Sequence = seq,
            Payload = payload
        };
    }
}
