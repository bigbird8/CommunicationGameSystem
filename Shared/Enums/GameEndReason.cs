namespace CommunicationGame.Shared.Enums;

public enum GameEndReason
{
    Win,
    Lose,
    ClientDisconnect,
    SourceError,
    ServerShutdown,
    Timeout,
    Error
}
