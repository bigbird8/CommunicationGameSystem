using System.Windows;
 using System.Windows.Documents;
 using System.Collections.Generic;

 namespace CommunicationGame.Client.Windows;

 public partial class LogWindow : Window
 {
    public LogWindow() : this(null) { }

    public LogWindow(IEnumerable<string>? history)
    {
        InitializeComponent();
        if (history != null)
        {
            foreach (var entry in history)
            {
                var para = new Paragraph(new Run(entry)) { Margin = new Thickness(0) };
                RtbLog.Document.Blocks.Add(para);
            }
            RtbLog.ScrollToEnd();
        }
    }

    public void AppendLog(string text)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => AppendLog(text));
            return;
        }

        var para = new Paragraph(new Run(text)) { Margin = new Thickness(0) };
        RtbLog.Document.Blocks.Add(para);
        RtbLog.ScrollToEnd();

        while (RtbLog.Document.Blocks.Count > 500)
            RtbLog.Document.Blocks.Remove(RtbLog.Document.Blocks.FirstBlock);
    }

    private void BtnClear_Click(object sender, RoutedEventArgs e)
    {
        RtbLog.Document.Blocks.Clear();
        RtbLog.Document.Blocks.Add(new Paragraph());
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }
}
