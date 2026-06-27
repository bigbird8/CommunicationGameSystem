namespace CommunicationGame.Shared.Protocol;

public static class ProtocolConstants
{
    public const int DefaultTcpPort = 5000;
    public const int DefaultBaudRate = 9600;
    public const int DefaultDataBits = 8;
    public const string DefaultComPort = "COM6";

    public const byte CobsDelimiter = 0x00;
    public const int MaxUartPayloadLength = 8;
    public const int UartPacketOverhead = 4;

    public const int HeartbeatIntervalMs = 3000;
    public const int HeartbeatTimeoutMs = 10000;
    public const int MaxMissedHeartbeats = 3;

    public const int TcpReadBufferSize = 4096;
    public const int MaxTcpMessageLength = 8192;

    public const string ClientVersion = "1.0";
    public const string ServerVersion = "1.0";
}
