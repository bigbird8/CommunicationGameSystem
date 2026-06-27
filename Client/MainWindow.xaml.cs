using System.Windows;
using System.Windows.Media;
using System.Windows.Documents;
using CommunicationGame.Client.Game;
using CommunicationGame.Client.Tcp;
using CommunicationGame.Client.Windows;
using CommunicationGame.Shared.Enums;
using CommunicationGame.Shared.Messages;
using CommunicationGame.Shared.Protocol;

namespace CommunicationGame.Client;

public partial class MainWindow : Window
{
    private TcpGameClient? _client;
    private readonly ClientGameEngine _engine = new();
    private readonly List<string> _logHistory = new();
    private LogWindow? _logWindow;
    private bool _isClosing;

    // Throttle for live pressure logging so the log shows activity during the
    // game without flooding it at the ~10 Hz data rate.
    private DateTime _lastPressureLog = DateTime.MinValue;
    private bool _lastLoggedInGreen;
    private static readonly TimeSpan PressureLogInterval = TimeSpan.FromSeconds(1);

    public MainWindow()
    {
        InitializeComponent();
        _engine.StateUpdated += () => OnUI(UpdateDisplay);
        AppendLog("Game client initialized. Connect to server to begin.");
    }

    private async void BtnConnect_Click(object sender, RoutedEventArgs e)
    {
        if (_client != null) return;

        _client = new TcpGameClient
        {
            Host = TxtHost.Text.Trim(),
            Port = int.TryParse(TxtPort.Text.Trim(), out var p) ? p : ProtocolConstants.DefaultTcpPort
        };

        // Log events fire from the TCP read/heartbeat background threads, so they
        // MUST be marshalled to the UI thread before touching any controls.
        // (Previously they ran directly on a background thread, which throws a
        // cross-thread exception and silently breaks logging once the read loop
        // is active — i.e. during the game.)
        _client.Log += msg => OnUI(() => AppendLog(msg));
        _client.Connected += () => OnUI(OnConnected);
        _client.Disconnected += () => OnUI(OnDisconnected);
        _client.MessageReceived += msg => OnUI(() => HandleMessage(msg));

        SetStatus("Connecting...", Brushes.Orange);
        BtnConnect.IsEnabled = false;

        bool ok = await _client.ConnectAsync();
        if (!ok)
        {
            SetStatus("Connection failed", Brushes.Red);
            BtnConnect.IsEnabled = true;
            _client.Dispose();
            _client = null;
            return;
        }

        _engine.State = ClientState.Connected;
        await _client.SendMessageAsync(TcpMessageFactory.Hello());
        _engine.State = ClientState.HandshakeSent;
        AppendLog("Sent HELLO to server.");
    }

    private void OnConnected()
    {
        SetStatus("Connected", Brushes.LimeGreen);
        BtnDisconnect.IsEnabled = true;
        BtnConnect.IsEnabled = false;
        TxtHost.IsEnabled = false;
        TxtPort.IsEnabled = false;
    }

    private void OnDisconnected()
    {
        SetStatus("Disconnected", Brushes.Gray);
        _engine.State = ClientState.Disconnected;
        BtnConnect.IsEnabled = true;
        BtnDisconnect.IsEnabled = false;
        BtnPause.IsEnabled = false;
        BtnResume.IsEnabled = false;
        BtnRestart.IsEnabled = false;
        TxtHost.IsEnabled = true;
        TxtPort.IsEnabled = true;
        _client?.Dispose();
        _client = null;
        AppendLog("Disconnected from server.");
    }

    private async void HandleMessage(TcpMessage msg)
    {
        switch (msg.Type)
        {
            case TcpMessageType.WELCOME:
                _engine.SessionId = msg.SessionId;
                _engine.State = ClientState.Ready;
                AppendLog($"Received WELCOME (session: {msg.SessionId}). Sending READY...");
                if (_client != null)
                    await _client.SendMessageAsync(TcpMessageFactory.Ready());
                _engine.State = ClientState.WaitingForGameStart;
                break;

            case TcpMessageType.GAME_START:
                _engine.Reset();
                _engine.State = ClientState.Playing;
                BtnPause.IsEnabled = true;
                BtnResume.IsEnabled = false;
                BtnRestart.IsEnabled = false;
                TxtResult.Text = "";
                SetStatus("Playing", Brushes.LimeGreen);
                AppendLog("Game started!");
                break;

            case TcpMessageType.PRESSURE_DATA:
                int pressure = msg.Pressure ?? 0;
                bool inGreen = msg.InGreen ?? false;
                double greenAccum = msg.GreenAccum ?? 0;
                double redConsec = msg.RedConsec ?? 0;

                _engine.UpdateFromServer(pressure, inGreen, greenAccum, redConsec);

                // Log on every green/red zone transition, and otherwise at most
                // once per second, so the log window reflects live gameplay.
                var nowTs = DateTime.Now;
                if (inGreen != _lastLoggedInGreen || nowTs - _lastPressureLog >= PressureLogInterval)
                {
                    _lastLoggedInGreen = inGreen;
                    _lastPressureLog = nowTs;
                    AppendLog($"Pressure {pressure,3} [{(inGreen ? "GREEN" : "RED")}]  green {greenAccum:F1}s / red {redConsec:F1}s");
                }
                break;

            case TcpMessageType.PAUSE_ACK:
                _engine.State = ClientState.Paused;
                BtnPause.IsEnabled = false;
                BtnResume.IsEnabled = true;
                SetStatus("Paused", Brushes.Orange);
                AppendLog("Game paused.");
                break;

            case TcpMessageType.RESUME_ACK:
                _engine.State = ClientState.Playing;
                BtnPause.IsEnabled = true;
                BtnResume.IsEnabled = false;
                SetStatus("Playing", Brushes.LimeGreen);
                AppendLog("Game resumed.");
                break;

            case TcpMessageType.RESTART_ACK:
                _engine.SessionId = msg.SessionId;
                _engine.Reset();
                AppendLog($"Restart acknowledged (new session: {msg.SessionId}).");
                break;

            case TcpMessageType.GAME_END:
                var result = msg.Result ?? GameResult.Lose;
                var reason = msg.Reason ?? GameEndReason.Error;
                _engine.SetGameEnd(result, reason);
                BtnPause.IsEnabled = false;
                BtnResume.IsEnabled = false;
                BtnRestart.IsEnabled = true;
                string resultText = result == GameResult.Win ? "YOU WIN!" : "GAME OVER";
                TxtResult.Text = resultText;
                TxtResult.Foreground = result == GameResult.Win
                    ? Brushes.Green : Brushes.Red;
                SetStatus("Game Ended", result == GameResult.Win ? Brushes.Green : Brushes.Red);
                AppendLog($"Game ended: {result} ({reason}).");
                break;

            case TcpMessageType.ERROR:
                AppendLog($"Server error: [{msg.ErrorCode}] {msg.Message}");
                break;

            case TcpMessageType.SERVER_SHUTDOWN:
                AppendLog("Server is shutting down.");
                break;
        }
    }

    private void UpdateDisplay()
    {
        TxtPressure.Text = _engine.Pressure.ToString();
        PressureBorder.Background = _engine.InGreen
            ? new LinearGradientBrush(Color.FromRgb(16, 185, 129), Color.FromRgb(5, 150, 105), 45)
            : new LinearGradientBrush(Color.FromRgb(239, 68, 68), Color.FromRgb(220, 38, 38), 45);
        PbGreen.Value = Math.Min(_engine.GreenAccum, GameConstants.GreenTargetSeconds);
        PbRed.Value = Math.Min(_engine.RedConsec, GameConstants.RedLimitSeconds);
        TxtGreenTime.Text = $"{_engine.GreenAccum:F1}s";
        TxtRedTime.Text = $"{_engine.RedConsec:F1}s";
    }

    private async void BtnPause_Click(object sender, RoutedEventArgs e)
    {
        if (_client != null)
            await _client.SendMessageAsync(TcpMessageFactory.PauseRequest());
    }

    private async void BtnResume_Click(object sender, RoutedEventArgs e)
    {
        if (_client != null)
            await _client.SendMessageAsync(TcpMessageFactory.ResumeRequest());
    }

    private async void BtnRestart_Click(object sender, RoutedEventArgs e)
    {
        if (_client != null)
            await _client.SendMessageAsync(TcpMessageFactory.RestartRequest());
    }

    private void BtnDisconnect_Click(object sender, RoutedEventArgs e)
    {
        _client?.Dispose();
        _client = null;
        OnDisconnected();
    }

    private void BtnLog_Click(object sender, RoutedEventArgs e)
    {
        if (_logWindow == null || !_logWindow.IsLoaded)
        {
            _logWindow = new LogWindow(_logHistory) { Owner = this };
        }
        _logWindow.Show();
        _logWindow.Activate();
    }

    private void SetStatus(string text, Brush color)
    {
        TxtStatus.Text = text;
        StatusIndicator.Fill = color;
    }

    private void AppendLog(string text)
    {
        if (_isClosing) return;
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        string fullText = $"[{timestamp}] {text}";

        // Store in history for later retrieval
        _logHistory.Add(fullText);
        if (_logHistory.Count > 500)
            _logHistory.RemoveAt(0);

        // Append to mini log (inline in main window)
        var miniPara = new Paragraph(new Run(fullText)) { Margin = new Thickness(0) };
        RtbMiniLog.Document.Blocks.Add(miniPara);
        RtbMiniLog.ScrollToEnd();
        while (RtbMiniLog.Document.Blocks.Count > 100)
            RtbMiniLog.Document.Blocks.Remove(RtbMiniLog.Document.Blocks.FirstBlock);

        // Also append to log window if open
        _logWindow?.AppendLog(fullText);
    }

    private void OnUI(Action action)
    {
        if (_isClosing) return;
        if (Dispatcher.CheckAccess()) action();
        else Dispatcher.BeginInvoke(action);
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _isClosing = true;
        _client?.Dispose();
        _logWindow?.Close();
    }
}
