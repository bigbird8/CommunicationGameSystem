using System.IO.Ports;
using CommunicationGame.Shared.Enums;
using CommunicationGame.Shared.Protocol;

namespace CommunicationGame.Server.Uart;

public sealed class UartManager : IDisposable
{
    private SerialPort? _serialPort;
    private CancellationTokenSource? _cts;
    private Task? _readTask;
    private Task? _heartbeatTask;
    private readonly List<byte> _receiveBuffer = new();
    private readonly object _lock = new();
    private byte _txSequence;
    private DateTime _lastPongReceived = DateTime.UtcNow;
    private int _missedPongs;
    private bool _disposed;

    public UartState State { get; private set; } = UartState.Disconnected;

    public event Action<string>? Log;
    public event Action<UartState>? StateChanged;
    public event Action<int>? DataReceived;
    public event Action? McuReady;
    public event Action<string>? ErrorOccurred;

    public string ComPort { get; set; } = ProtocolConstants.DefaultComPort;
    public int BaudRate { get; set; } = ProtocolConstants.DefaultBaudRate;

    public async Task<bool> ConnectAsync()
    {
        try
        {
            _cts = new CancellationTokenSource();

            _serialPort = new SerialPort(ComPort, BaudRate, Parity.None, ProtocolConstants.DefaultDataBits, StopBits.One)
            {
                ReadTimeout = 1000,
                WriteTimeout = 1000
            };
            _serialPort.Open();
            OnLog($"Serial port {ComPort} opened at {BaudRate} baud.");

            _readTask = Task.Run(() => ReadLoop(_cts.Token));

            SetState(UartState.Handshaking);
            await SendPacketAsync(UartPacketType.HELLO);
            OnLog("Sent HELLO to MCU, waiting for handshake...");

            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (State == UartState.Handshaking && DateTime.UtcNow < deadline)
            {
                await Task.Delay(100);
            }

            if (State == UartState.Connected)
            {
                _heartbeatTask = Task.Run(() => HeartbeatLoop(_cts.Token));
                OnLog("UART handshake complete. MCU connected.");
                return true;
            }

            OnLog("UART handshake timed out.");
            SetState(UartState.Error);
            return false;
        }
        catch (Exception ex)
        {
            OnLog($"UART connect error: {ex.Message}");
            SetState(UartState.Error);
            return false;
        }
    }

    public async Task SendStartStreamAsync()
    {
        await SendPacketAsync(UartPacketType.START_STREAM);
        SetState(UartState.Streaming);
        OnLog("Sent START_STREAM to MCU.");
    }

    public async Task SendStopStreamAsync()
    {
        await SendPacketAsync(UartPacketType.STOP_STREAM);
        if (State == UartState.Streaming)
            SetState(UartState.Connected);
        OnLog("Sent STOP_STREAM to MCU.");
    }

    private void ReadLoop(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _serialPort?.IsOpen == true)
            {
                try
                {
                    int b = _serialPort.ReadByte();
                    if (b == -1) continue;

                    if (b == ProtocolConstants.CobsDelimiter)
                    {
                        if (_receiveBuffer.Count > 0)
                        {
                            ProcessFrame(_receiveBuffer.ToArray());
                            _receiveBuffer.Clear();
                        }
                    }
                    else
                    {
                        _receiveBuffer.Add((byte)b);
                    }
                }
                catch (TimeoutException)
                {
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested)
            {
                OnLog($"UART read error: {ex.Message}");
                SetState(UartState.Error);
                ErrorOccurred?.Invoke(ex.Message);
            }
        }
    }

    private void ProcessFrame(byte[] cobsFrame)
    {
        var packet = UartPacket.Decode(cobsFrame);
        if (packet == null)
        {
            OnLog("UART: Invalid packet (COBS/CRC failure).");
            return;
        }

        switch (packet.Type)
        {
            case UartPacketType.HELLO:
                OnLog("MCU sent HELLO (reset detected).");
                if (State == UartState.Streaming)
                    ErrorOccurred?.Invoke("MCU reset during streaming");
                SetState(UartState.Handshaking);
                _ = SendPacketAsync(UartPacketType.WELCOME);
                break;

            case UartPacketType.READY:
                if (State == UartState.Handshaking)
                {
                    SetState(UartState.Connected);
                    McuReady?.Invoke();
                }
                break;

            case UartPacketType.DATA:
                if (State == UartState.Streaming && packet.Payload.Length >= 1)
                {
                    int value = packet.Payload[0];
                    if (value >= GameConstants.MinPressureValue && value <= GameConstants.MaxPressureValue)
                    {
                        DataReceived?.Invoke(value);
                    }
                    else
                    {
                        OnLog($"UART: Invalid pressure value {value}.");
                    }
                }
                break;

            case UartPacketType.PONG:
                _lastPongReceived = DateTime.UtcNow;
                _missedPongs = 0;
                break;

            case UartPacketType.ERROR:
                OnLog("MCU reported ERROR.");
                ErrorOccurred?.Invoke("MCU error packet received");
                break;

            default:
                OnLog($"UART: Unexpected packet type {packet.Type}.");
                break;
        }
    }

    private async Task HeartbeatLoop(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(ProtocolConstants.HeartbeatIntervalMs, ct);

                if (State == UartState.Disconnected || State == UartState.Error)
                    break;

                await SendPacketAsync(UartPacketType.PING);

                var elapsed = DateTime.UtcNow - _lastPongReceived;
                if (elapsed.TotalMilliseconds > ProtocolConstants.HeartbeatTimeoutMs)
                {
                    _missedPongs++;
                    OnLog($"UART heartbeat: missed PONG #{_missedPongs}.");

                    if (_missedPongs >= ProtocolConstants.MaxMissedHeartbeats)
                    {
                        OnLog("UART heartbeat timeout — MCU unresponsive.");
                        SetState(UartState.Error);
                        ErrorOccurred?.Invoke("UART heartbeat timeout");
                        break;
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task SendPacketAsync(UartPacketType type, byte[]? payload = null)
    {
        var packet = new UartPacket
        {
            Type = type,
            Sequence = _txSequence++,
            Payload = payload ?? Array.Empty<byte>()
        };

        var frame = packet.Encode();

        lock (_lock)
        {
            if (_serialPort?.IsOpen == true)
            {
                _serialPort.Write(frame, 0, frame.Length);
            }
        }

        await Task.CompletedTask;
    }

    private void SetState(UartState newState)
    {
        if (State == newState) return;
        State = newState;
        OnLog($"UART state → {newState}");
        StateChanged?.Invoke(newState);
    }

    private void OnLog(string msg) => Log?.Invoke($"[UART] {msg}");

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cts?.Cancel();

        try { _serialPort?.Close(); } catch { }
        try { _serialPort?.Dispose(); } catch { }

        _cts?.Dispose();
    }
}
