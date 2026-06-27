using CommunicationGame.Shared.Enums;
using CommunicationGame.Shared.Protocol;

namespace CommunicationGame.Client.Game;

/// <summary>
/// Client-side display engine. Mirrors server state from PRESSURE_DATA messages.
/// Also tracks local timers as backup display source.
/// </summary>
public class ClientGameEngine
{
    public ClientState State { get; set; } = ClientState.Disconnected;
    public int Pressure { get; private set; }
    public bool InGreen { get; private set; }
    public double GreenAccum { get; private set; }
    public double RedConsec { get; private set; }
    public GameResult? Result { get; private set; }
    public GameEndReason? EndReason { get; private set; }
    public string? SessionId { get; set; }

    public event Action? StateUpdated;

    public void UpdateFromServer(int pressure, bool inGreen, double greenAccum, double redConsec)
    {
        Pressure = pressure;
        InGreen = inGreen;
        GreenAccum = greenAccum;
        RedConsec = redConsec;
        StateUpdated?.Invoke();
    }

    public void SetGameEnd(GameResult result, GameEndReason reason)
    {
        Result = result;
        EndReason = reason;
        State = ClientState.GameEnded;
        StateUpdated?.Invoke();
    }

    public void Reset()
    {
        Pressure = 0;
        InGreen = false;
        GreenAccum = 0;
        RedConsec = 0;
        Result = null;
        EndReason = null;
    }
}
