using CommunicationGame.Shared.Enums;
using CommunicationGame.Shared.Protocol;

namespace CommunicationGame.Server.Game;

/// <summary>
/// Server-authoritative game session. Tracks green/red timers and determines win/lose.
/// Adapted from the original RehabGame GameEngine with consecutive-timing rules.
/// </summary>
public class GameSession
{
    public string SessionId { get; } = Guid.NewGuid().ToString("N")[..8];
    public GameState State { get; private set; } = GameState.Idle;
    public double GreenAccum { get; private set; }
    public double RedConsec { get; private set; }
    public int LastPressure { get; private set; }
    public bool LastInGreen { get; private set; }
    public DateTime StartTime { get; private set; }

    public event Action<GameResult, GameEndReason>? GameEnded;
    public event Action<int, bool, double, double>? PressureProcessed;
    public event Action<string>? Log;

    public void Start()
    {
        GreenAccum = 0;
        RedConsec = 0;
        LastPressure = 0;
        LastInGreen = false;
        StartTime = DateTime.UtcNow;
        State = GameState.Running;
        OnLog($"Game session {SessionId} started.");
    }

    public void Pause()
    {
        if (State != GameState.Running) return;
        State = GameState.Paused;
        OnLog("Game paused.");
    }

    public void Resume()
    {
        if (State != GameState.Paused) return;
        State = GameState.Running;
        OnLog("Game resumed.");
    }

    public void ProcessPressure(int pressure, double elapsedSeconds)
    {
        if (State != GameState.Running) return;
        if (elapsedSeconds <= 0) return;

        LastPressure = pressure;
        bool inGreen = pressure >= GameConstants.GreenMinPressure
                    && pressure <= GameConstants.GreenMaxPressure;
        LastInGreen = inGreen;

        if (inGreen)
        {
            GreenAccum += elapsedSeconds;
            RedConsec = 0;
        }
        else
        {
            RedConsec += elapsedSeconds;
        }

        PressureProcessed?.Invoke(pressure, inGreen, GreenAccum, RedConsec);

        if (GreenAccum >= GameConstants.GreenTargetSeconds)
        {
            EndGame(GameResult.Win, GameEndReason.Win);
        }
        else if (RedConsec >= GameConstants.RedLimitSeconds)
        {
            EndGame(GameResult.Lose, GameEndReason.Lose);
        }
    }

    public void EndGame(GameResult result, GameEndReason reason)
    {
        if (State == GameState.Ended) return;
        State = GameState.Ended;
        OnLog($"Game ended: {result} ({reason}).");
        GameEnded?.Invoke(result, reason);
    }

    private void OnLog(string msg) => Log?.Invoke($"[Game:{SessionId}] {msg}");
}
