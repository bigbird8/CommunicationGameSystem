namespace CommunicationGame.Shared.Enums;

public enum ClientState
{
    Disconnected,
    Connecting,
    Connected,
    HandshakeSent,
    Ready,
    WaitingForGameStart,
    Playing,
    Paused,
    GameEnded,
    Disconnecting,
    Error
}
