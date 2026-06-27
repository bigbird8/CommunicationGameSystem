namespace CommunicationGame.Shared.Enums;

public enum GameState
{
    Idle,
    WaitingForClient,
    Handshaking,
    Ready,
    Running,
    Paused,
    Ended,
    Error
}
