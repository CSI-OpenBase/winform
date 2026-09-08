using System.Diagnostics;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace CSI.OpenBase.Desktop;

internal sealed class MainForm : Form
{
    private static readonly Color TextColor = Color.FromArgb(31, 35, 40);
    private static readonly Color MutedTextColor = Color.FromArgb(87, 96, 106);
    private static readonly Color BorderColor = Color.FromArgb(216, 222, 228);
    private static readonly Color AccentColor = Color.FromArgb(9, 105, 218);

    private readonly AppLog _log;
    private readonly DesktopSettings _settings;
    private readonly BackendLauncher _backend;
    private readonly CancellationTokenSource _applicationCancellation = new();
    private readonly SemaphoreSlim _restartLock = new(1, 1);
    private readonly WebView2 _webView;
    private readonly TextBox _workspaceTextBox;
    private readonly Label _statusLabel;
    private readonly ProgressBar _progressBar;
    private readonly Button _browseButton;
    private readonly Button _restartButton;
    private readonly System.Windows.Forms.Timer _backendMonitor;
    private bool _webViewReady;
    private bool _connected;
    private bool _allowClose;
    private bool _closing;

    public MainForm(AppLog log)
    {
        _log = log;
        _settings = DesktopSettings.Load(log);
        _backend = new BackendLauncher(log);

        Text = "CSI OpenBase";
        BackColor = Color.White;
        ForeColor = TextColor;
        Font = new Font("Segoe UI", 9F);
        MinimumSize = new Size(820, 560);
        Size = new Size(1180, 780);
        StartPosition = FormStartPosition.CenterScreen;

        var header = new TableLayoutPanel
        {
            BackColor = Color.White,
            ColumnCount = 2,
            Dock = DockStyle.Top,
            Height = 105,
            Padding = new Padding(20, 14, 20, 12),
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 39F));

        var title = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 14F),
            ForeColor = TextColor,
            Margin = new Padding(0, 2, 0, 0),
            Text = "CSI OpenBase",
        };
        header.Controls.Add(title, 0, 0);

        var actions = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0),
            WrapContents = false,
        };
        var logButton = CreateButton("打开日志", 82);
        logButton.Click += (_, _) => OpenLog();
        _restartButton = CreateButton("重新启动", 82, primary: true);
        _restartButton.Click += async (_, _) => await RestartBackendAsync();
        actions.Controls.AddRange([logButton, _restartButton]);
        header.Controls.Add(actions, 1, 0);

        var workspaceRow = new TableLayoutPanel
        {
            ColumnCount = 3,
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
        };
        workspaceRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        workspaceRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        workspaceRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var workspaceLabel = new Label
        {
            Anchor = AnchorStyles.Left,
            AutoSize = true,
            ForeColor = MutedTextColor,
            Margin = new Padding(0, 0, 10, 0),
            Text = "工作目录",
        };
        _workspaceTextBox = new TextBox
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            BackColor = Color.White,
            ReadOnly = true,
            Text = _settings.WorkspaceDirectory,
        };
        _browseButton = CreateButton("选择目录", 82);
        _browseButton.Margin = new Padding(10, 3, 0, 3);
        _browseButton.Click += async (_, _) => await SelectWorkspaceAsync();
        workspaceRow.Controls.Add(workspaceLabel, 0, 0);
        workspaceRow.Controls.Add(_workspaceTextBox, 1, 0);
        workspaceRow.Controls.Add(_browseButton, 2, 0);
        header.Controls.Add(workspaceRow, 0, 1);
        header.SetColumnSpan(workspaceRow, 2);

        var separator = new Panel
        {
            BackColor = BorderColor,
            Dock = DockStyle.Top,
            Height = 1,
        };

        var statusPanel = new TableLayoutPanel
        {
            BackColor = Color.FromArgb(246, 248, 250),
            ColumnCount = 2,
            Dock = DockStyle.Top,
            Height = 38,
            Padding = new Padding(20, 8, 20, 7),
        };
        statusPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        statusPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120F));
        _statusLabel = new Label
        {
            AutoEllipsis = true,
            Dock = DockStyle.Fill,
            ForeColor = MutedTextColor,
            Text = "准备启动...",
        };
        _progressBar = new ProgressBar
        {
            Dock = DockStyle.Fill,
            MarqueeAnimationSpeed = 24,
            Style = ProgressBarStyle.Marquee,
        };
        statusPanel.Controls.Add(_statusLabel, 0, 0);
        statusPanel.Controls.Add(_progressBar, 1, 0);

        _webView = new WebView2
        {
            AllowExternalDrop = false,
            BackColor = Color.White,
            Dock = DockStyle.Fill,
        };

        Controls.Add(_webView);
        Controls.Add(statusPanel);
        Controls.Add(separator);
        Controls.Add(header);

        _backendMonitor = new System.Windows.Forms.Timer { Interval = 2_000 };
        _backendMonitor.Tick += (_, _) =>
        {
            if (_connected && !_backend.IsRunning)
            {
                _connected = false;
                SetStatus(
                    "本地服务意外退出。请打开日志查看原因，然后重新启动。",
                    isBusy: false,
                    isError: true);
            }
        };
        _backendMonitor.Start();

        Shown += async (_, _) => await InitializeAndStartAsync();
    }

    protected override async void OnFormClosing(FormClosingEventArgs eventArgs)
    {
        if (_allowClose)
        {
            base.OnFormClosing(eventArgs);
            return;
        }

        eventArgs.Cancel = true;
        if (_closing)
        {
            return;
        }

        _closing = true;
        _connected = false;
        _backendMonitor.Stop();
        Enabled = false;
        SetStatus("正在关闭本地服务...", isBusy: true);
        _applicationCancellation.Cancel();
        await _restartLock.WaitAsync();
        try
        {
            await _backend.DisposeAsync();
        }
        finally
        {
            _restartLock.Release();
            _allowClose = true;
            Close();
        }
    }

    private static Button CreateButton(string text, int width, bool primary = false)
    {
        return new Button
        {
            AutoSize = false,
            BackColor = primary ? AccentColor : Color.White,
            FlatStyle = FlatStyle.Flat,
            ForeColor = primary ? Color.White : TextColor,
            Height = 30,
            Margin = new Padding(8, 0, 0, 0),
            Text = text,
            UseVisualStyleBackColor = false,
            Width = width,
            FlatAppearance =
            {
                BorderColor = primary ? AccentColor : BorderColor,
                BorderSize = 1,
            },
        };
    }

    private async Task InitializeAndStartAsync()
    {
        try
        {
            SetStatus("正在初始化网页组件...", isBusy: true);
            var webViewDataDirectory = Path.Combine(DesktopSettings.ApplicationDataDirectory, "webview2");
            var environment = await CreateWebViewEnvironmentAsync(webViewDataDirectory);
            await _webView.EnsureCoreWebView2Async(environment);
            _webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            _webView.CoreWebView2.NewWindowRequested += (_, eventArgs) =>
            {
                eventArgs.Handled = true;
                if (Uri.TryCreate(eventArgs.Uri, UriKind.Absolute, out var uri) &&
                    uri.Scheme is "http" or "https")
                {
                    Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
                }
            };
            _webViewReady = true;
        }
        catch (Exception exception)
        {
            _log.Write("desktop", $"WebView2 initialization failed: {exception}");
            SetStatus(
                "无法初始化 WebView2。请安装或修复 Microsoft Edge WebView2 Runtime，然后重新启动。",
                isBusy: false,
                isError: true);
            return;
        }

        await RestartBackendAsync();
    }

    private async Task<CoreWebView2Environment> CreateWebViewEnvironmentAsync(
        string userDataDirectory)
    {
        try
        {
            return await CoreWebView2Environment.CreateAsync(
                userDataFolder: userDataDirectory);
        }
        catch (Exception firstFailure)
        {
            var bootstrapper = Path.Combine(
                AppContext.BaseDirectory, "MicrosoftEdgeWebView2Setup.exe");
            if (!File.Exists(bootstrapper))
            {
                throw new InvalidOperationException(
                    "Microsoft Edge WebView2 Runtime 未安装，且程序目录中没有安装引导程序。",
                    firstFailure);
            }

            SetStatus("正在安装 Microsoft Edge WebView2 Runtime...", isBusy: true);
            _log.Write("desktop", "WebView2 Runtime is unavailable; launching the bundled bootstrapper");
            using var installer = Process.Start(
                new ProcessStartInfo
                {
                    FileName = bootstrapper,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    Arguments = "/silent /install",
                }) ?? throw new InvalidOperationException("无法启动 WebView2 安装引导程序。");
            await installer.WaitForExitAsync(_applicationCancellation.Token);
            _log.Write("desktop", $"WebView2 bootstrapper exited with code {installer.ExitCode}");

            try
            {
                return await CoreWebView2Environment.CreateAsync(
                    userDataFolder: userDataDirectory);
            }
            catch (Exception retryFailure)
            {
                throw new InvalidOperationException(
                    $"WebView2 Runtime 安装后仍无法初始化（安装程序退出代码 {installer.ExitCode}）。",
                    new AggregateException(firstFailure, retryFailure));
            }
        }
    }

    private async Task RestartBackendAsync()
    {
        if (!_webViewReady || _applicationCancellation.IsCancellationRequested)
        {
            return;
        }

        await _restartLock.WaitAsync();
        try
        {
            if (_applicationCancellation.IsCancellationRequested)
            {
                return;
            }

            _connected = false;
            SetControlsEnabled(false);
            BackendLaunchPlan plan;
            try
            {
                plan = _backend.ResolveLaunchPlan();
            }
            catch (Exception exception)
            {
                _log.Write("desktop", exception.Message);
                SetStatus(exception.Message, isBusy: false, isError: true);
                return;
            }

            var progress = new Progress<string>(message => SetStatus(message, isBusy: true));
            try
            {
                var address = await _backend.StartAsync(
                    plan,
                    _settings.WorkspaceDirectory,
                    progress,
                    _applicationCancellation.Token);
                _applicationCancellation.Token.ThrowIfCancellationRequested();
                var desktopToken = _backend.DesktopToken
                    ?? throw new InvalidOperationException("本地服务未提供桌面会话令牌。");
                var cookie = _webView.CoreWebView2.CookieManager.CreateCookie(
                    "csi_desktop",
                    desktopToken,
                    address.Host,
                    "/");
                cookie.IsHttpOnly = true;
                cookie.IsSecure = false;
                _webView.CoreWebView2.CookieManager.AddOrUpdateCookie(cookie);
                _webView.Source = address;
                _connected = true;
                SetStatus($"已连接 · {plan.DisplayName}", isBusy: false);
            }
            catch (OperationCanceledException) when (_applicationCancellation.IsCancellationRequested)
            {
                // The application is closing.
            }
            catch (Exception exception)
            {
                _connected = false;
                _log.Write("desktop", $"Backend startup failed: {exception}");
                SetStatus(exception.Message, isBusy: false, isError: true);
            }
        }
        finally
        {
            if (!_applicationCancellation.IsCancellationRequested)
            {
                SetControlsEnabled(true);
            }
            _restartLock.Release();
        }
    }

    private async Task SelectWorkspaceAsync()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择 CSI OpenBase 工作目录",
            InitialDirectory = _settings.WorkspaceDirectory,
            ShowNewFolderButton = true,
            UseDescriptionForTitle = true,
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            var selectedPath = Path.GetFullPath(dialog.SelectedPath);
            Directory.CreateDirectory(selectedPath);
            var writeProbe = Path.Combine(selectedPath, $".csi-write-{Guid.NewGuid():N}.tmp");
            await File.WriteAllTextAsync(writeProbe, string.Empty);
            File.Delete(writeProbe);

            _settings.WorkspaceDirectory = selectedPath;
            _settings.Save();
            _workspaceTextBox.Text = selectedPath;
            _log.Write("desktop", $"Data home changed to {selectedPath}");
            await RestartBackendAsync();
        }
        catch (Exception exception)
        {
            _log.Write("desktop", $"Workspace selection failed: {exception}");
            SetStatus($"无法使用所选目录：{exception.Message}", isBusy: false, isError: true);
        }
    }

    private void OpenLog()
    {
        try
        {
            Process.Start(new ProcessStartInfo(_log.LogFilePath) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                $"无法打开日志。\r\n\r\n{_log.LogFilePath}\r\n\r\n{exception.Message}",
                "打开日志",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void SetControlsEnabled(bool enabled)
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        _browseButton.Enabled = enabled;
        _restartButton.Enabled = enabled;
    }

    private void SetStatus(string message, bool isBusy, bool isError = false)
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        _statusLabel.Text = message.ReplaceLineEndings(" ");
        _statusLabel.ForeColor = isError ? Color.FromArgb(207, 34, 46) : MutedTextColor;
        _progressBar.Visible = isBusy;
        _progressBar.Style = isBusy ? ProgressBarStyle.Marquee : ProgressBarStyle.Blocks;
    }
}
