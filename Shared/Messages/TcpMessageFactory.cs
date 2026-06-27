using CommunicationGame.Shared.Enums;
using CommunicationGame.Shared.Protocol;

namespace CommunicationGame.Shared.Messages;

public static class TcpMessageFactory
{
    public static TcpMessage Hello() => new()
    {
        Type = TcpMessageType.HELLO,
        Version = ProtocolConstants.ClientVersion
    };

    public static TcpMessage Welcome(string sessionId) => new()
    {
        Type = TcpMessageType.WELCOME,
        Version = ProtocolConstants.ServerVersion,
        SessionId = sessionId
    };

    public static TcpMessage Ready() => new()
    {
        Type = TcpMessageType.READY
    };

    public static TcpMessage GameStart(string sessionId) => new()
    {
        Type = TcpMessageType.GAME_START,
        SessionId = sessionId
    };

    public static TcpMessage PressureData(int pressure, bool inGreen, double greenAccum, double redConsec) => new()
    {
        Type = TcpMessageType.PRESSURE_DATA,
        Pressure = pressure,
        InGreen = inGreen,
        GreenAccum = Math.Round(greenAccum, 2),
        RedConsec = Math.Round(redConsec, 2)
    };

    public static TcpMessage GameEnd(GameResult result, GameEndReason reason) => new()
    {
        Type = TcpMessageType.GAME_END,
        Result = result,
        Reason = reason
    };

    public static TcpMessage PauseRequest() => new()
    {
        Type = TcpMessageType.PAUSE_REQUEST
    };

    public static TcpMessage PauseAck() => new()
    {
        Type = TcpMessageType.PAUSE_ACK
    };

    public static TcpMessage ResumeRequest() => new()
    {
        Type = TcpMessageType.RESUME_REQUEST
    };

    public static TcpMessage ResumeAck() => new()
    {
        Type = TcpMessageType.RESUME_ACK
    };

    public static TcpMessage RestartRequest() => new()
    {
        Type = TcpMessageType.RESTART_REQUEST
    };

    public static TcpMessage RestartAck(string newSessionId) => new()
    {
        Type = TcpMessageType.RESTART_ACK,
        SessionId = newSessionId
    };

    public static TcpMessage Ping() => new()
    {
        Type = TcpMessageType.HEARTBEAT_PING
    };

    public static TcpMessage Pong() => new()
    {
        Type = TcpMessageType.HEARTBEAT_PONG
    };

    public static TcpMessage Error(string errorCode, string message) => new()
    {
        Type = TcpMessageType.ERROR,
        ErrorCode = errorCode,
        Message = message
    };

    public static TcpMessage ServerShutdown() => new()
    {
        Type = TcpMessageType.SERVER_SHUTDOWN,
        Message = "Server is shutting down"
    };
}
