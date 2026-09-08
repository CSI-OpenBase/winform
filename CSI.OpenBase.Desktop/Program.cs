namespace CSI.OpenBase.Desktop;

internal static class Program
{
    private const string MutexName = @"Local\CSI.OpenBase.Desktop.SingleInstance";

    [STAThread]
    private static void Main()
    {
        using var singleInstance = new Mutex(true, MutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show(
                "CSI OpenBase 已在运行。请切换到已打开的窗口。",
                "CSI OpenBase",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();

        try
        {
            using var log = AppLog.Create();
            Application.Run(new MainForm(log));
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"CSI OpenBase 无法启动。\r\n\r\n{exception.Message}",
                "启动失败",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
