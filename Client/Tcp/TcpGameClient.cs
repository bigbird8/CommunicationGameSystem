using System.Net.Sockets;
using System.Text;
using CommunicationGame.Shared.Enums;
using CommunicationGame.Shared.Messages;
using CommunicationGame.Shared.Protocol;

namespace CommunicationGame.Client.Tcp;

public sealed class TcpGameClient : IDisposable
{
    private TcpClient? _client;
    private NetworkStream? _stream;
    private CancellationTokenSource? _cts;
    private Task? _readTask;
    private Task? _heartbeatTask;
    private readonly StringBuilder _lineBuffer = new();
    private DateTime _lastPingReceived = DateTime.UtcNow;
    private bool _disposed;

    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = ProtocolConstants.DefaultTcpPort;
    public bool IsConnected => _client?.Connected == true;

    public event Action<string>? Log;
    public event Action? Connected;
    public event Action? Disconnected;
    public event Action<TcpMessage>? MessageReceived;

    public async Task<bool> ConnectAsync()
    {
        try
        {
            _cts = new CancellationTokenSource();
            _client = new TcpClient { NoDelay = true };
            await _client.ConnectAsync(Host, Port);
            _stream = _client.GetStream();
            _lastPingReceived = DateTime.UtcNow;

            OnLog($"Connected to {Host}:{Port}.");
            Connected?.Invoke();

            _readTask = Task.Run(() => ReadLoop(_cts.Token));
            _heartbeatTask = Task.Run(() => HeartbeatMonitorLoop(_cts.Token));
            return true;
        }
        catch (Exception ex)
        {
            OnLog($"Connect failed: {ex.Message}");
            return false;
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
                if (read == 0) break;

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
                OnLog($"Read error: {ex.Message}");
        }

        HandleDisconnect();
    }

    private void ProcessLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        try
        {
            var msg = TcpMessageSerializer.Deserialize(line);
            if (msg == null) { OnLog($"Bad JSON: {line[..Math.Min(line.Length, 60)]}"); return; }

            if (msg.Type == TcpMessageType.HEARTBEAT_PING)
            {
                _lastPingReceived = DateTime.UtcNow;
                _ = SendMessageAsync(TcpMessageFactory.Pong());
                return;
            }

            MessageReceived?.Invoke(msg);
        }
        catch (Exception ex) { OnLog($"Parse error: {ex.Message}"); }
    }

    private async Task HeartbeatMonitorLoop(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && IsConnected)
            {
                await Task.Delay(ProtocolConstants.HeartbeatIntervalMs, ct);
                if ((DateTime.UtcNow - _lastPingReceived).TotalMilliseconds > ProtocolConstants.HeartbeatTimeoutMs * 2)
                {
                    OnLog("Server heartbeat timeout.");
                    break;
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
        catch (Exception ex) { OnLog($"Send error: {ex.Message}"); }
    }

    private void HandleDisconnect()
    {
        Close();
        Disconnected?.Invoke();
    }

    public void Close()
    {
        try { _stream?.Close(); } catch { }
        _stream = null;
        try { _client?.Close(); } catch { }
        _client = null;
    }

    private void OnLog(string msg) => Log?.Invoke($"[TCP] {msg}");

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts?.Cancel();
        Close();
        _cts?.Dispose();
    }
}
