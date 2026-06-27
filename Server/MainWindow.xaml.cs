using System.IO.Ports;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using CommunicationGame.Server.Bridge;
using CommunicationGame.Shared.Enums;
using CommunicationGame.Shared.Protocol;

namespace CommunicationGame.Server;

public partial class MainWindow : Window
{
    private ProtocolBridge? _bridge;
    private readonly DispatcherTimer _comRefreshTimer;
    private bool _isRunning;
    private bool _isClosing;

    public MainWindow()
    {
        InitializeComponent();
        RefreshComPorts();

        _comRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _comRefreshTimer.Tick += (s, e) => { if (!_isRunning) RefreshComPorts(); };
        _comRefreshTimer.Start();

        AppendLog("Server UI initialized. Configure settings and click Start.");
    }

    private void RefreshComPorts()
    {
        var ports = SerialPort.GetPortNames();
        var selected = CmbComPort.SelectedItem?.ToString();

        CmbComPort.Items.Clear();
        foreach (var port in ports)
            CmbComPort.Items.Add(port);

        if (ports.Length > 0)
        {
            if (!string.IsNullOrEmpty(selected) && ports.Contains(selected))
                CmbComPort.SelectedItem = selected;
            else
                CmbComPort.SelectedIndex = 0;
        }
    }

    private async void BtnStart_Click(object sender, RoutedEventArgs e)
    {
        if (_isRunning) return;

        string comPort = CmbComPort.SelectedItem?.ToString() ?? ProtocolConstants.DefaultComPort;
        int baudRate = int.Parse(((ComboBoxItem)CmbBaudRate.SelectedItem).Content.ToString()!);
        int dataBits = int.Parse(((ComboBoxItem)CmbDataBits.SelectedItem).Content.ToString()!);
        var parity = Enum.Parse<Parity>(((ComboBoxItem)CmbParity.SelectedItem).Content.ToString()!);
        var stopBits = ((ComboBoxItem)CmbStopBits.SelectedItem).Content.ToString() == "One" ? StopBits.One : StopBits.Two;
        int tcpPort = int.TryParse(TxtTcpPort.Text, out var p) ? p : int.Parse(ProtocolConstants.DefaultComPort);

        _bridge = new ProtocolBridge
        {
            ComPort = comPort,
            BaudRate = baudRate,
            TcpPort = tcpPort
        };

        _bridge.Log += msg => Dispatcher.BeginInvoke(() => AppendLog(msg));

        AppendLog($"Starting server: {comPort} @ {baudRate} baud, TCP port {tcpPort}...");

        await _bridge.StartAsync();

        _isRunning = true;
        BtnStart.IsEnabled = false;
        BtnStop.IsEnabled = true;
        CmbComPort.IsEnabled = false;
        CmbBaudRate.IsEnabled = false;
        CmbDataBits.IsEnabled = false;
        CmbParity.IsEnabled = false;
        CmbStopBits.IsEnabled = false;
        TxtTcpPort.IsEnabled = false;

        SetHeaderStatus("Running", Brushes.LimeGreen);
        UpdateStatus();

        var statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        statusTimer.Tick += (s, ev) =>
        {
            if (_isRunning) UpdateStatus();
            else statusTimer.Stop();
        };
        statusTimer.Start();
    }

    private async void BtnStop_Click(object sender, RoutedEventArgs e)
    {
        if (!_isRunning || _bridge == null) return;

        AppendLog("Stopping server...");
        await _bridge.ShutdownAsync();
        _bridge.Dispose();
        _bridge = null;

        _isRunning = false;
        BtnStart.IsEnabled = true;
        BtnStop.IsEnabled = false;
        CmbComPort.IsEnabled = true;
        CmbBaudRate.IsEnabled = true;
        CmbDataBits.IsEnabled = true;
        CmbParity.IsEnabled = true;
        CmbStopBits.IsEnabled = true;
        TxtTcpPort.IsEnabled = true;

        SetHeaderStatus("Offline", Brushes.Red);
        TxtUartStatus.Text = "Disconnected";
        TxtUartStatus.Foreground = Brushes.Red;
        TxtTcpStatus.Text = "Not listening";
        TxtTcpStatus.Foreground = Brushes.Red;
        TxtGameStatus.Text = "Idle";
        TxtGameStatus.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139));

        AppendLog("Server stopped.");
    }

    private void UpdateStatus()
    {
        if (_bridge == null) return;

        var uartState = _bridge.GetUartState();
        TxtUartStatus.Text = uartState.ToString();
        TxtUartStatus.Foreground = uartState switch
        {
            UartState.Connected or UartState.Streaming => Brushes.LimeGreen,
            UartState.Handshaking => Brushes.Orange,
            UartState.Error => Brushes.Red,
            _ => Brushes.Gray
        };

        bool tcpClientConnected = _bridge.IsTcpClientConnected();
        TxtTcpStatus.Text = tcpClientConnected ? "Client connected" : "Listening (no client)";
        TxtTcpStatus.Foreground = tcpClientConnected ? Brushes.LimeGreen : Brushes.Orange;

        var gameState = _bridge.GetGameState();
        TxtGameStatus.Text = gameState.ToString();
        TxtGameStatus.Foreground = gameState switch
        {
            GameState.Running => Brushes.LimeGreen,
            GameState.Paused => Brushes.Orange,
            GameState.Ended => Brushes.Yellow,
            GameState.Error => Brushes.Red,
            _ => new SolidColorBrush(Color.FromRgb(100, 116, 139))
        };
    }

    private void SetHeaderStatus(string text, Brush color)
    {
        HeaderStatusText.Text = text;
        HeaderStatusDot.Fill = color;
    }

    private void AppendLog(string text)
    {
        if (_isClosing) return;
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        var para = new Paragraph(new Run($"[{timestamp}] {text}")) { Margin = new Thickness(0) };
        RtbLog.Document.Blocks.Add(para);
        RtbLog.ScrollToEnd();

        while (RtbLog.Document.Blocks.Count > 1000)
            RtbLog.Document.Blocks.Remove(RtbLog.Document.Blocks.FirstBlock);
    }

    private void BtnClearLog_Click(object sender, RoutedEventArgs e)
    {
        RtbLog.Document.Blocks.Clear();
        RtbLog.Document.Blocks.Add(new Paragraph());
    }

    private async void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_isClosing) return;

        _isClosing = true;
        _comRefreshTimer.Stop();

        if (_isRunning && _bridge != null)
        {
            await _bridge.ShutdownAsync();
            _bridge.Dispose();
        }
    }
}
