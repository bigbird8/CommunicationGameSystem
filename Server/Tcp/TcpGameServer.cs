using System.Net;
using System.Net.Sockets;
using System.Text;
using CommunicationGame.Shared.Enums;
using CommunicationGame.Shared.Messages;
using CommunicationGame.Shared.Protocol;

namespace CommunicationGame.Server.Tcp;

public sealed class TcpGameServer : IDisposable
{
    private TcpListener? _listener;
    private TcpClient? _client;
    private NetworkStream? _stream;
    private CancellationTokenSource? _cts;
    private Task? _readTask;
    private Task? _heartbeatTask;
    private readonly StringBuilder _lineBuffer = new();
    private DateTime _lastPongReceived = DateTime.UtcNow;
    private int _missedPongs;
    private bool _disposed;

    public int Port { get; set; } = ProtocolConstants.DefaultTcpPort;
    public bool IsClientConnected => _client?.Connected == true;

    public event Action<string>? Log;
    public event Action? ClientConnected;
    public event Action? ClientDisconnected;
    public event Action<TcpMessage>? MessageReceived;
    public event Action<string>? ErrorOccurred;

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Any, Port);
        _listener.Start();
        OnLog($"TCP server listening on port {Port}.");

        Task.Run(() => AcceptLoop(_cts.Token));
    }

    private async Task AcceptLoop(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var client = await _listener!.AcceptTcpClientAsync(ct);
                client.NoDelay = true;

                CloseCurrentClient();

                _client = client;
                _stream = client.GetStream();
                _lastPongReceived = DateTime.UtcNow;
                _missedPongs = 0;

                OnLog("TCP client connected.");
                ClientConnected?.Invoke();

                _readTask = Task.Run(() => ReadLoop(ct));
                _heartbeatTask = Task.Run(() => HeartbeatLoop(ct));
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested)
                OnLog($"TCP accept error: {ex.Message}");
        }
    }

    private async Task ReadLoop(CancellationToken ct)
    {
        var buffer = new byte[ProtocolConstants.TcpReadBufferSize];
        try
        {
            while (!ct.IsCancellationRequested && _stream != null)
            {
                int read = await _stream.ReadAsync(buffer, 0, buffer.Length, ct);
                if (read == 0)
                {
                    OnLog("TCP client disconnected (read 0).");
                    break;
                }

                string chunk = Encoding.UTF8.GetString(buffer, 0, read);
                foreach (char ch in chunk)
                {
                    if (ch == '\n')
                    {
                        ProcessLine(_lineBuffer.ToString());
                        _lineBuffer.Clear();
                    }
                    else if (ch != '\r')
                    {
                        _lineBuffer.Append(ch);
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested)
                OnLog($"TCP read error: {ex.Message}");
        }

        HandleClientDisconnect();
    }

    private void ProcessLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;

        try
        {
            var msg = TcpMessageSerializer.Deserialize(line);
            if (msg == null)
            {
                OnLog($"TCP: Failed to parse JSON: {line[..Math.Min(line.Length, 80)]}");
                return;
            }

            if (msg.Type == TcpMessageType.HEARTBEAT_PONG)
            {
                _lastPongReceived = DateTime.UtcNow;
                _missedPongs = 0;
                return;
            }

            MessageReceived?.Invoke(msg);
        }
        catch (Exception ex)
        {
            OnLog($"TCP: JSON parse error: {ex.Message}");
        }
    }

    private async Task HeartbeatLoop(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && IsClientConnected)
            {
                await Task.Delay(ProtocolConstants.HeartbeatIntervalMs, ct);
                await SendMessageAsync(TcpMessageFactory.Ping());

                var elapsed = DateTime.UtcNow - _lastPongReceived;
                if (elapsed.TotalMilliseconds > ProtocolConstants.HeartbeatTimeoutMs)
                {
                    _missedPongs++;
                    OnLog($"TCP heartbeat: missed PONG #{_missedPongs}.");

                    if (_missedPongs >= ProtocolConstants.MaxMissedHeartbeats)
                    {
                        OnLog("TCP heartbeat timeout — client unresponsive.");
                        ErrorOccurred?.Invoke("Client heartbeat timeout");
                        break;
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    public async Task SendMessageAsync(TcpMessage message)
    {
        if (_stream == null || !_stream.CanWrite) return;

        try
        {
            var data = Encoding.UTF8.GetBytes(TcpMessageSerializer.ToLine(message));
            await _stream.WriteAsync(data);
            await _stream.FlushAsync();
        }
        catch (Exception ex)
        {
            OnLog($"TCP send error: {ex.Message}");
        }
    }

    private void HandleClientDisconnect()
    {
        CloseCurrentClient();
        ClientDisconnected?.Invoke();
    }

    private void CloseCurrentClient()
    {
        try { _stream?.Close(); } catch { }
        _stream = null;
        try { _client?.Close(); } catch { }
        _client = null;
        _lineBuffer.Clear();
    }

    private void OnLog(string msg) => Log?.Invoke($"[TCP] {msg}");

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cts?.Cancel();
        CloseCurrentClient();
        try { _listener?.Stop(); } catch { }

        _cts?.Dispose();
    }
}
