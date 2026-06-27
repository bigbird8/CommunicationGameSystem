namespace CommunicationGame.Shared.Enums;

public enum UartPacketType : byte
{
    HELLO = 0x01,
    WELCOME = 0x02,
    READY = 0x03,
    START_STREAM = 0x04,
    STOP_STREAM = 0x05,
    DATA = 0x06,
    PING = 0x07,
    PONG = 0x08,
    ERROR = 0x09,
    ACK = 0x0A
}
