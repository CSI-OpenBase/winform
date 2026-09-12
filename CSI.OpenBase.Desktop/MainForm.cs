using System.Diagnostics;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace CSI.OpenBase.Desktop;

internal sealed class MainForm : Form
{
    private const int TaskPanelWidth = 340;
    private const string TaskBridgeScript = """
        (() => {
          if (window.__csiDesktopTaskBridgeInstalled) return;
          window.__csiDesktopTaskBridgeInstalled = true;

          const clean = (value, maximumLength) => {
            if (value === null || value === undefined) return null;
            const normalized = String(value).replace(/\s+/g, ' ').trim();
            return normalized ? normalized.slice(0, maximumLength) : null;
          };
          const project = (state) => {
            const jobs = Array.isArray(state?.jobs)
              ? state.jobs.slice(0, 100).flatMap((job) => {
                  const id = Number(job?.id);
                  if (!Number.isSafeInteger(id) || id <= 0) return [];
                  return [{
                    id,
                    kind: clean(job.kind, 64) || 'task',
                    status: clean(job.status, 64) || 'unknown',
                    video_id: clean(job.video_id, 64),
                    message: clean(job.message, 500),
                    created_at: clean(job.created_at, 64),
                    started_at: clean(job.started_at, 64),
                    finished_at: clean(job.finished_at, 64)
                  }];
                })
              : [];
            const activeJobs = Number(state?.active_jobs);
            const latestJobId = Number(state?.latest_job_id);
            return {
              active_jobs: Number.isSafeInteger(activeJobs) && activeJobs >= 0
                ? activeJobs
                : jobs.filter((job) => job.status === 'queued' || job.status === 'running').length,
              latest_job_id: Number.isSafeInteger(latestJobId) && latestJobId > 0
                ? latestJobId
                : (jobs.length ? jobs[0].id : null),
              jobs
            };
          };
          const publish = (message) => {
            try {
              window.chrome.webview.postMessage(JSON.stringify(message));
            } catch (_) {
              // The native host may already be shutting down.
            }
          };
          const publishState = (state) => publish({ type: 'task-state', state: project(state) });
          const originalFetch = window.fetch.bind(window);

          window.fetch = async (...args) => {
            const response = await originalFetch(...args);
            try {
              const input = args[0];
              const rawUrl = typeof input === 'string' ? input : input?.url;
              if (rawUrl &&
                  new URL(rawUrl, document.baseURI).pathname === '/api/state' &&
                  response.ok) {
                const originalJson = response.json.bind(response);
                response.json = async () => {
                  try {
                    const state = await originalJson();
                    publishState(state);
                    return state;
                  } catch (error) {
                    publish({ type: 'task-error' });
                    throw error;
                  }
                };
              }
            } catch (_) {
              // Preserve the page's fetch behavior even if mirroring fails.
            }
            return response;
          };
        })()
        """;
    private const string TaskDocumentStateScript = """
        (() => {
          const root = document.querySelector('.history-band tbody');
          if (!root) return null;
          const kindByLabel = {
            '账号授权': 'authorize',
            '数据导出': 'export',
            '主页同步': 'sync_videos',
            '评论导出': 'comments'
          };
          const text = (element) => (element?.innerText || '').trim();
          const jobs = Array.from(root.querySelectorAll('tr')).flatMap((row) => {
            const cells = row.querySelectorAll('td');
            if (cells.length < 6) return [];
            const id = Number(text(cells[0]).replace(/[^0-9]/g, ''));
            if (!Number.isSafeInteger(id) || id <= 0) return [];
            const typeLabel = text(cells[1]);
            const statusElement = cells[3].querySelector('.job-status');
            const statusClass = Array.from(statusElement?.classList || [])
              .find((value) => value.startsWith('status-'));
            const videoId = text(cells[2].querySelector('code'));
            return [{
              id,
              kind: kindByLabel[typeLabel] || 'task',
              status: statusClass ? statusClass.slice(7) : 'unknown',
              video_id: videoId && videoId !== '—' ? videoId : null,
              message: text(cells[5]) || null,
              created_at: text(cells[4]) || null,
              started_at: null,
              finished_at: null
            }];
          });
          const activeJobs = Number(document.body?.dataset.activeJobs || 0);
          return {
            active_jobs: Number.isSafeInteger(activeJobs) && activeJobs >= 0 ? activeJobs : 0,
            latest_job_id: jobs.length ? jobs[0].id : null,
            jobs
          };
        })()
        """;
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
    private readonly TableLayoutPanel _contentLayout;
    private readonly Panel _taskPanelHost;
    private readonly TaskPanelControl _taskPanel;
    private readonly TextBox _workspaceTextBox;
    private readonly Label _statusLabel;
    private readonly ProgressBar _progressBar;
    private readonly Button _browseButton;
    private readonly AccessibleButton _tasksButton;
    private readonly Button _restartButton;
    private readonly System.Windows.Forms.Timer _backendMonitor;
    private bool _webViewReady;
    private bool _connected;
    private bool _taskPanelVisible = true;
    private bool _taskRefreshRunning;
    private bool _taskReadErrorLogged;
    private bool _taskStateStale;
    private int _activeTaskCount;
    private int _taskConnectionGeneration;
    private Uri? _backendAddress;
    private bool _allowClose;
    private bool _closing;

    public MainForm(AppLog log)
    {
        _log = log;
        _settings = DesktopSettings.Load(log);
        _backend = new BackendLauncher(log);
        _taskPanel = new TaskPanelControl();

        Text = "CSI OpenBase";
        BackColor = Color.White;
        ForeColor = TextColor;
        Font = new Font("Segoe UI", 9F);
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        KeyPreview = true;
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
        _tasksButton = CreateButton("任务", 92);
        _tasksButton.AccessibleName = "显示或隐藏任务面板";
        _tasksButton.Click += async (_, _) =>
        {
            SetTaskPanelVisible(!_taskPanelVisible);
            if (_taskPanelVisible)
            {
                await RefreshTaskPanelFromDocumentAsync();
                _taskPanel.FocusContent();
            }
        };
        _restartButton = CreateButton("重新启动", 82, primary: true);
        _restartButton.Click += async (_, _) => await RestartBackendAsync();
        actions.Controls.AddRange([logButton, _tasksButton, _restartButton]);
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
            Text = _settings.HasPersistedWorkspace ? _settings.WorkspaceDirectory : "尚未设置",
        };
        _browseButton = CreateButton("选择目录", 82);
        _browseButton.Margin = new Padding(10, 3, 0, 3);
        _browseButton.Click += async (_, _) =>
        {
            await SelectWorkspaceAsync();
        };
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

        _taskPanel.CloseRequested += (_, _) =>
        {
            SetTaskPanelVisible(false);
            _tasksButton.Focus();
        };
        _taskPanel.ShowDisconnected("等待本地服务启动");

        _taskPanelHost = new Panel
        {
            BackColor = BorderColor,
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            Padding = new Padding(1, 0, 0, 0),
        };
        _taskPanelHost.Controls.Add(_taskPanel);

        _contentLayout = new TableLayoutPanel
        {
            BackColor = Color.White,
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            Padding = new Padding(0),
            RowCount = 1,
        };
        _contentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _contentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, TaskPanelWidth));
        _contentLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        _contentLayout.Controls.Add(_webView, 0, 0);
        _contentLayout.Controls.Add(_taskPanelHost, 1, 0);

        Controls.Add(_contentLayout);
        Controls.Add(statusPanel);
        Controls.Add(separator);
        Controls.Add(header);

        _backendMonitor = new System.Windows.Forms.Timer { Interval = 2_000 };
        _backendMonitor.Tick += (_, _) =>
        {
            if (_connected && !_backend.IsRunning)
            {
                _connected = false;
                _backendAddress = null;
                ResetTaskPanel("本地服务已断开");
                SetStatus(
                    "本地服务意外退出。请打开日志查看原因，然后重新启动。",
                    isBusy: false,
                    isError: true);
                return;
            }
        };
        _backendMonitor.Start();
        _restartButton.Enabled = _settings.HasPersistedWorkspace;
        UpdateTaskButtonAppearance();

        Shown += async (_, _) =>
        {
            ApplyTaskPanelWidth();
            await InitializeAndStartAsync();
        };
        KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.KeyCode == Keys.Escape && _taskPanelVisible)
            {
                SetTaskPanelVisible(false);
                _tasksButton.Focus();
                eventArgs.Handled = true;
                eventArgs.SuppressKeyPress = true;
            }
        };
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
        _backendAddress = null;
        _backendMonitor.Stop();
        ResetTaskPanel("应用正在关闭");
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

    private static AccessibleButton CreateButton(string text, int width, bool primary = false)
    {
        return new AccessibleButton
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
        if (!_settings.HasPersistedWorkspace)
        {
            SetStatus("首次使用，请先选择工作目录。", isBusy: false);
            await SelectWorkspaceAsync(restartBackend: false);
        }

        try
        {
            SetStatus("正在初始化网页组件...", isBusy: true);
            var webViewDataDirectory = Path.Combine(DesktopSettings.ApplicationDataDirectory, "webview2");
            var environment = await CreateWebViewEnvironmentAsync(webViewDataDirectory);
            await _webView.EnsureCoreWebView2Async(environment);
            _webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            await _webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                TaskBridgeScript);
            _webView.CoreWebView2.WebMessageReceived += (_, eventArgs) =>
                HandleTaskBridgeMessage(eventArgs);
            _webView.CoreWebView2.NavigationCompleted += async (_, eventArgs) =>
            {
                if (!_connected)
                {
                    return;
                }

                if (!eventArgs.IsSuccess)
                {
                    _taskStateStale = true;
                    _taskPanel.ShowError("当前页面未能加载");
                    UpdateTaskButtonAppearance();
                    return;
                }

                await RefreshTaskPanelFromDocumentAsync();
            };
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
            ResetTaskPanel("网页组件初始化失败");
            SetStatus(
                "无法初始化 WebView2。请安装或修复 Microsoft Edge WebView2 Runtime，然后重新启动。",
                isBusy: false,
                isError: true);
            return;
        }

        if (!_settings.HasPersistedWorkspace)
        {
            SetControlsEnabled(true);
            ResetTaskPanel("请选择工作目录后启动本地服务");
            SetStatus(
                "尚未设置工作目录。请选择目录后再启动本地服务。",
                isBusy: false);
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

        if (!_settings.HasPersistedWorkspace)
        {
            SetControlsEnabled(true);
            ResetTaskPanel("请选择工作目录后启动本地服务");
            SetStatus(
                "尚未设置工作目录。请选择目录后再启动本地服务。",
                isBusy: false);
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
            _backendAddress = null;
            PrepareTaskPanelForConnection();
            SetControlsEnabled(false);
            BackendLaunchPlan plan;
            try
            {
                plan = _backend.ResolveLaunchPlan();
            }
            catch (Exception exception)
            {
                _log.Write("desktop", exception.Message);
                ResetTaskPanel("本地服务未启动");
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
                _backendAddress = address;
                _connected = true;
                _webView.Source = address;
                SetStatus($"已连接 · {plan.DisplayName}", isBusy: false);
            }
            catch (OperationCanceledException) when (_applicationCancellation.IsCancellationRequested)
            {
                // The application is closing.
            }
            catch (Exception exception)
            {
                _connected = false;
                _backendAddress = null;
                ResetTaskPanel("本地服务未连接");
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

    private async Task<bool> SelectWorkspaceAsync(bool restartBackend = true)
    {
        var requiresInitialSetup = !_settings.HasPersistedWorkspace;
        var initialDirectory = Directory.Exists(_settings.WorkspaceDirectory)
            ? _settings.WorkspaceDirectory
            : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        using var dialog = new FolderBrowserDialog
        {
            Description = requiresInitialSetup
                ? "首次使用：选择保存用户数据的工作目录"
                : "选择 CSI OpenBase 工作目录",
            InitialDirectory = initialDirectory,
            ShowNewFolderButton = true,
            UseDescriptionForTitle = true,
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            if (requiresInitialSetup)
            {
                SetStatus(
                    "尚未设置工作目录。请选择目录后再启动本地服务。",
                    isBusy: false);
            }

            return false;
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
            if (restartBackend)
            {
                await RestartBackendAsync();
            }

            return true;
        }
        catch (Exception exception)
        {
            _log.Write("desktop", $"Workspace selection failed: {exception}");
            SetStatus($"无法使用所选目录：{exception.Message}", isBusy: false, isError: true);
            return false;
        }
    }

    private void SetTaskPanelVisible(bool visible)
    {
        if (_taskPanelVisible == visible)
        {
            return;
        }

        _taskPanelVisible = visible;
        _contentLayout.SuspendLayout();
        try
        {
            _contentLayout.ColumnStyles[1].Width = visible
                ? ScaledTaskPanelWidth()
                : 0F;
            _taskPanelHost.Visible = visible;
        }
        finally
        {
            _contentLayout.ResumeLayout(performLayout: true);
        }

        UpdateTaskButtonAppearance();
    }

    private void ApplyTaskPanelWidth()
    {
        if (_taskPanelVisible)
        {
            _contentLayout.ColumnStyles[1].Width = ScaledTaskPanelWidth();
        }
    }

    private float ScaledTaskPanelWidth()
    {
        return TaskPanelWidth * Math.Max(DeviceDpi, 96) / 96F;
    }

    private void UpdateTaskButtonAppearance()
    {
        var count = _activeTaskCount > 99 ? "99+" : _activeTaskCount.ToString();
        _tasksButton.Text = _activeTaskCount > 0 ? $"任务 {count}" : "任务";
        _tasksButton.BackColor = _taskPanelVisible
            ? Color.FromArgb(234, 242, 252)
            : Color.White;
        _tasksButton.ForeColor = _taskPanelVisible ? AccentColor : TextColor;
        _tasksButton.FlatAppearance.BorderColor = _taskPanelVisible
            ? (_taskStateStale ? Color.FromArgb(207, 34, 46) : AccentColor)
            : BorderColor;
        var accessibleName = _taskPanelVisible
            ? "隐藏任务面板"
            : "显示任务面板";
        var accessibleDescription = _activeTaskCount > 0
            ? $"{_activeTaskCount} 个任务正在进行{(_taskStateStale ? "，状态可能已过期" : "")}"
            : (_taskStateStale ? "任务状态暂不可用" : "没有正在进行的任务");
        _tasksButton.UpdateAccessibleText(accessibleName, accessibleDescription);
    }

    private void PrepareTaskPanelForConnection()
    {
        _taskConnectionGeneration++;
        _taskRefreshRunning = false;
        _activeTaskCount = 0;
        _taskReadErrorLogged = false;
        _taskStateStale = false;
        _taskPanel.ShowLoading("正在连接本地服务...");
        UpdateTaskButtonAppearance();
    }

    private void ResetTaskPanel(string message)
    {
        _taskConnectionGeneration++;
        _taskRefreshRunning = false;
        _activeTaskCount = 0;
        _taskReadErrorLogged = false;
        _taskStateStale = false;
        _taskPanel.ShowDisconnected(message);
        UpdateTaskButtonAppearance();
    }

    private async Task RefreshTaskPanelFromDocumentAsync()
    {
        if (!_connected || _taskRefreshRunning || IsDisposed || Disposing)
        {
            return;
        }

        var connectionGeneration = _taskConnectionGeneration;
        _taskRefreshRunning = true;
        try
        {
            var json = await _webView.CoreWebView2.ExecuteScriptAsync(
                TaskDocumentStateScript);
            if (!_connected ||
                connectionGeneration != _taskConnectionGeneration)
            {
                return;
            }

            if (string.Equals(json, "null", StringComparison.Ordinal))
            {
                throw new InvalidDataException("当前页面未包含任务列表。");
            }

            ApplyTaskState(BackendTaskState.Parse(json));
        }
        catch (OperationCanceledException) when (
            _applicationCancellation.IsCancellationRequested ||
            !_connected ||
            connectionGeneration != _taskConnectionGeneration)
        {
            // A backend restart or application shutdown invalidated this refresh.
        }
        catch (Exception exception)
        {
            if (!_connected ||
                _applicationCancellation.IsCancellationRequested ||
                connectionGeneration != _taskConnectionGeneration)
            {
                return;
            }

            MarkTaskPanelReadError(exception);
        }
        finally
        {
            if (connectionGeneration == _taskConnectionGeneration)
            {
                _taskRefreshRunning = false;
            }
        }
    }

    private void HandleTaskBridgeMessage(CoreWebView2WebMessageReceivedEventArgs eventArgs)
    {
        if (!_connected ||
            IsDisposed ||
            Disposing ||
            !IsCurrentBackendSource(eventArgs.Source))
        {
            return;
        }

        try
        {
            var message = eventArgs.TryGetWebMessageAsString();
            using var document = JsonDocument.Parse(message);
            var root = document.RootElement;
            if (!root.TryGetProperty("type", out var typeElement))
            {
                return;
            }

            switch (typeElement.GetString())
            {
                case "task-state" when root.TryGetProperty("state", out var stateElement):
                    ApplyTaskState(BackendTaskState.Parse(stateElement.GetRawText()));
                    break;
                case "task-error":
                    MarkTaskPanelReadError();
                    break;
            }
        }
        catch (Exception exception)
        {
            MarkTaskPanelReadError(exception);
        }
    }

    private bool IsCurrentBackendSource(string source)
    {
        if (_backendAddress is null ||
            !Uri.TryCreate(source, UriKind.Absolute, out var sourceAddress))
        {
            return false;
        }

        return string.Equals(
                sourceAddress.Scheme,
                _backendAddress.Scheme,
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                sourceAddress.IdnHost,
                _backendAddress.IdnHost,
                StringComparison.OrdinalIgnoreCase) &&
            sourceAddress.Port == _backendAddress.Port;
    }

    private void ApplyTaskState(BackendTaskState state)
    {
        _activeTaskCount = state.ActiveTaskCount;
        _taskReadErrorLogged = false;
        _taskStateStale = false;
        _taskPanel.SetState(state);
        UpdateTaskButtonAppearance();
    }

    private void MarkTaskPanelReadError(Exception? exception = null)
    {
        if (!_taskReadErrorLogged)
        {
            var detail = exception is null ? string.Empty : $": {exception.Message}";
            _log.Write("desktop", $"Task panel refresh failed{detail}");
            _taskReadErrorLogged = true;
        }

        _taskStateStale = true;
        _taskPanel.ShowError("无法读取最新任务状态");
        UpdateTaskButtonAppearance();
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
        _restartButton.Enabled = enabled && _settings.HasPersistedWorkspace;
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

    private sealed class AccessibleButton : Button
    {
        public void UpdateAccessibleText(string name, string description)
        {
            var nameChanged = !string.Equals(AccessibleName, name, StringComparison.Ordinal);
            var descriptionChanged = !string.Equals(
                AccessibleDescription,
                description,
                StringComparison.Ordinal);
            AccessibleName = name;
            AccessibleDescription = description;
            if (!IsHandleCreated)
            {
                return;
            }

            if (nameChanged)
            {
                AccessibilityNotifyClients(AccessibleEvents.NameChange, -1);
            }

            if (descriptionChanged)
            {
                AccessibilityNotifyClients(AccessibleEvents.DescriptionChange, -1);
            }
        }
    }
}
