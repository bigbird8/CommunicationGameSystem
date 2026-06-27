using CommunicationGame.Server.Game;
using CommunicationGame.Server.Tcp;
using CommunicationGame.Server.Uart;
using CommunicationGame.Shared.Enums;
using CommunicationGame.Shared.Messages;
using CommunicationGame.Shared.Protocol;

namespace CommunicationGame.Server.Bridge;

/// <summary>
/// Bridges UART ↔ GameSession ↔ TCP. Central orchestrator for the server.
/// </summary>
public class ProtocolBridge : IDisposable
{
    private readonly UartManager _uart = new();
    private readonly TcpGameServer _tcp = new();
    private GameSession? _session;
    private DateTime _lastDataTime;
    private bool _disposed;

    public event Action<string>? Log;

    public string ComPort { get => _uart.ComPort; set => _uart.ComPort = value; }
    public int BaudRate { get => _uart.BaudRate; set => _uart.BaudRate = value; }
    public int TcpPort { get => _tcp.Port; set => _tcp.Port = value; }

    public UartState GetUartState() => _uart.State;
    public bool IsTcpClientConnected() => _tcp.IsClientConnected;
    public GameState GetGameState() => _session?.State ?? GameState.Idle;

    public async Task StartAsync()
    {
        _uart.Log += OnLog;
        _uart.DataReceived += OnUartData;
        _uart.ErrorOccurred += OnUartError;

        _tcp.Log += OnLog;
        _tcp.ClientConnected += OnClientConnected;
        _tcp.ClientDisconnected += OnClientDisconnected;
        _tcp.MessageReceived += OnTcpMessage;
        _tcp.ErrorOccurred += OnTcpError;

        bool uartOk = await _uart.ConnectAsync();
        if (!uartOk)
        {
            OnLog("[Bridge] UART connection failed. Starting TCP server anyway for testing.");
        }

        _tcp.Start();
        OnLog("[Bridge] Server started. Waiting for client...");
    }

    /// <summary>
    /// The game may start only when every link is up: the UART/MCU is connected
    /// (or already streaming) AND a TCP client is connected. This enforces the
    /// "start only if all connections are made" rule.
    /// </summary>
    private bool AllConnectionsReady()
    {
        bool uartReady = _uart.State == UartState.Connected || _uart.State == UartState.Streaming;
        return uartReady && _tcp.IsClientConnected;
    }

    private void OnClientConnected()
    {
        OnLog("[Bridge] Client connected — waiting for HELLO.");
    }

    private void OnClientDisconnected()
    {
        OnLog("[Bridge] Client disconnected.");
        if (_session?.State == GameState.Running || _session?.State == GameState.Paused)
        {
            _session.EndGame(GameResult.Lose, GameEndReason.ClientDisconnect);
            _ = _uart.SendStopStreamAsync();
        }
        _session = null;
    }

    private async void OnTcpMessage(TcpMessage msg)
    {
        switch (msg.Type)
        {
            case TcpMessageType.HELLO:
                _session = new GameSession();
                _session.Log += OnLog;
                _session.GameEnded += OnGameEnded;
                _session.PressureProcessed += OnPressureProcessed;

                await _tcp.SendMessageAsync(TcpMessageFactory.Welcome(_session.SessionId));
                OnLog($"[Bridge] Sent WELCOME (session {_session.SessionId}).");
                break;

            case TcpMessageType.READY:
                if (_session != null && _session.State == GameState.Idle)
                {
                    if (!AllConnectionsReady())
                    {
                        OnLog("[Bridge] Game start refused — not all connections are ready (UART must be connected).");
                        await _tcp.SendMessageAsync(TcpMessageFactory.Error(
                            "NOT_READY",
                            $"Cannot start game: UART is '{_uart.State}'. The MCU must be connected before the game can start."));
                        break;
                    }

                    _session.Start();
                    _lastDataTime = DateTime.UtcNow;

                    await _uart.SendStartStreamAsync();

                    await _tcp.SendMessageAsync(TcpMessageFactory.GameStart(_session.SessionId));
                    OnLog("[Bridge] Game started.");
                }
                break;

            case TcpMessageType.PAUSE_REQUEST:
                if (_session?.State == GameState.Running)
                {
                    _session.Pause();
                    await _uart.SendStopStreamAsync();
                    await _tcp.SendMessageAsync(TcpMessageFactory.PauseAck());
                }
                break;

            case TcpMessageType.RESUME_REQUEST:
                if (_session?.State == GameState.Paused)
                {
                    _session.Resume();
                    _lastDataTime = DateTime.UtcNow;
                    await _uart.SendStartStreamAsync();
                    await _tcp.SendMessageAsync(TcpMessageFactory.ResumeAck());
                }
                break;

            case TcpMessageType.RESTART_REQUEST:
                if (_session?.State == GameState.Ended || _session?.State == GameState.Error)
                {
                    if (!AllConnectionsReady())
                    {
                        OnLog("[Bridge] Restart refused — not all connections are ready (UART must be connected).");
                        await _tcp.SendMessageAsync(TcpMessageFactory.Error(
                            "NOT_READY",
                            $"Cannot restart game: UART is '{_uart.State}'. The MCU must be connected before the game can start."));
                        break;
                    }

                    _session = new GameSession();
                    _session.Log += OnLog;
                    _session.GameEnded += OnGameEnded;
                    _session.PressureProcessed += OnPressureProcessed;

                    await _tcp.SendMessageAsync(TcpMessageFactory.RestartAck(_session.SessionId));
                    _session.Start();
                    _lastDataTime = DateTime.UtcNow;

                    await _uart.SendStartStreamAsync();

                    await _tcp.SendMessageAsync(TcpMessageFactory.GameStart(_session.SessionId));
                    OnLog("[Bridge] Game restarted.");
                }
                break;

            case TcpMessageType.HEARTBEAT_PONG:
                break;

            default:
                OnLog($"[Bridge] Unexpected TCP message type: {msg.Type}");
                break;
        }
    }

    private void OnUartData(int pressure)
    {
        if (_session?.State != GameState.Running) return;

        var now = DateTime.UtcNow;
        double elapsed = (now - _lastDataTime).TotalSeconds;
        _lastDataTime = now;

        if (elapsed > 2.0) elapsed = 0.1;

        _session.ProcessPressure(pressure, elapsed);
    }

    private async void OnPressureProcessed(int pressure, bool inGreen, double greenAccum, double redConsec)
    {
        await _tcp.SendMessageAsync(TcpMessageFactory.PressureData(pressure, inGreen, greenAccum, redConsec));
    }

    private async void OnGameEnded(GameResult result, GameEndReason reason)
    {
        await _uart.SendStopStreamAsync();
        await _tcp.SendMessageAsync(TcpMessageFactory.GameEnd(result, reason));
        OnLog($"[Bridge] Sent GAME_END to client: {result} ({reason}).");
    }

    private async void OnUartError(string error)
    {
        OnLog($"[Bridge] UART error: {error}");
        if (_session?.State == GameState.Running || _session?.State == GameState.Paused)
        {
            _session.EndGame(GameResult.Lose, GameEndReason.SourceError);
            await _tcp.SendMessageAsync(TcpMessageFactory.Error("UART_ERROR", error));
        }
    }

    private async void OnTcpError(string error)
    {
        OnLog($"[Bridge] TCP error: {error}");
        if (_session?.State == GameState.Running || _session?.State == GameState.Paused)
        {
            _session.EndGame(GameResult.Lose, GameEndReason.Error);
            await _uart.SendStopStreamAsync();
        }
    }

    public async Task ShutdownAsync()
    {
        OnLog("[Bridge] Shutting down...");
        if (_session?.State == GameState.Running)
        {
            _session.EndGame(GameResult.Lose, GameEndReason.ServerShutdown);
        }

        await _tcp.SendMessageAsync(TcpMessageFactory.ServerShutdown());

        if (_uart.State == UartState.Streaming)
            await _uart.SendStopStreamAsync();

        _tcp.Dispose();
        _uart.Dispose();
    }

    private void OnLog(string msg) => Log?.Invoke(msg);

    public void Dispose()
    {
        if (_disposed) return;
        _tcp.Dispose();
        _uart.Dispose();
        // Suppress finalization so the GC doesn't call the finalizer
        GC.SuppressFinalize(this);
        _disposed = true;
    }
}
