namespace CommunicationGame.Shared.Enums;

public enum TcpMessageType
{
    HELLO,
    WELCOME,
    READY,
    GAME_START,
    PRESSURE_DATA,
    GAME_END,
    PAUSE_REQUEST,
    PAUSE_ACK,
    RESUME_REQUEST,
    RESUME_ACK,
    RESTART_REQUEST,
    RESTART_ACK,
    HEARTBEAT_PING,
    HEARTBEAT_PONG,
    ERROR,
    SERVER_SHUTDOWN
}
