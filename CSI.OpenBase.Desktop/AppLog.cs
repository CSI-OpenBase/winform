using System.Text;

namespace CSI.OpenBase.Desktop;

internal sealed class AppLog : IDisposable
{
    private readonly object _sync = new();
    private readonly Queue<string> _recentLines = new();
    private readonly StreamWriter _writer;

    private AppLog(string logFilePath)
    {
        LogFilePath = logFilePath;
        _writer = new StreamWriter(logFilePath, append: true, new UTF8Encoding(false))
        {
            AutoFlush = true,
        };
    }

    public string LogFilePath { get; }

    public static AppLog Create()
    {
        var logDirectory = Path.Combine(DesktopSettings.ApplicationDataDirectory, "logs");
        Directory.CreateDirectory(logDirectory);
        var path = Path.Combine(logDirectory, $"desktop-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        var log = new AppLog(path);
        log.Write("desktop", $"CSI OpenBase desktop starting ({Environment.Version}, {Environment.OSVersion})");
        return log;
    }

    public void Write(string source, string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var line = $"{DateTimeOffset.Now:O} [{source}] {message.TrimEnd()}";
        lock (_sync)
        {
            _writer.WriteLine(line);
            _recentLines.Enqueue(line);
            while (_recentLines.Count > 80)
            {
                _recentLines.Dequeue();
            }
        }
    }

    public string RecentText(int lineCount = 12)
    {
        lock (_sync)
        {
            return string.Join(Environment.NewLine, _recentLines.TakeLast(lineCount));
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _writer.Dispose();
        }
    }
}
