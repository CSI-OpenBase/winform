using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CSI.OpenBase.Desktop;

internal sealed record BackendLaunchPlan(
    string FileName,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    string DisplayName);

internal sealed class BackendLauncher : IAsyncDisposable
{
    private sealed record ShutdownResult(bool Accepted, bool ForceRequired);

    private readonly AppLog _log;
    private readonly HttpClient _httpClient;
    private readonly BackendProcessJob _processJob;
    private Process? _process;
    private CancellationTokenSource? _lifetimeCancellation;
    private string? _desktopToken;
    private string? _instanceNonce;

    public BackendLauncher(AppLog log)
    {
        _log = log;
        _processJob = new BackendProcessJob();
        _httpClient = new HttpClient(new HttpClientHandler { UseProxy = false })
        {
            Timeout = TimeSpan.FromSeconds(10),
        };
    }

    public Uri? BaseAddress { get; private set; }

    public string? DesktopToken => _desktopToken;

    public bool IsRunning
    {
        get
        {
            try
            {
                return _process is { HasExited: false };
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }

    public BackendLaunchPlan ResolveLaunchPlan()
    {
        var applicationDirectory = AppContext.BaseDirectory;
        var configuredBackend = Environment.GetEnvironmentVariable("CSI_OPENBASE_BACKEND");
        if (!string.IsNullOrWhiteSpace(configuredBackend))
        {
            var backendPath = Path.GetFullPath(
                Environment.ExpandEnvironmentVariables(configuredBackend.Trim()));
            if (!File.Exists(backendPath))
            {
                throw new FileNotFoundException(
                    $"CSI_OPENBASE_BACKEND 指向的文件不存在：{backendPath}",
                    backendPath);
            }

            return new BackendLaunchPlan(
                backendPath,
                [],
                Path.GetDirectoryName(backendPath)!,
                Path.GetFileName(backendPath));
        }

        var executableCandidates = new[]
        {
            Path.Combine(applicationDirectory, "backend", "CSI.OpenBase.Backend.exe"),
            Path.Combine(applicationDirectory, "CSI.OpenBase.Backend.exe"),
            Path.Combine(applicationDirectory, "csi-openbase-backend.exe"),
            Path.Combine(applicationDirectory, "openbase-backend.exe"),
            Path.Combine(applicationDirectory, "run_openbase.exe"),
            Path.Combine(applicationDirectory, "backend.exe"),
        };

        var backendExecutable = executableCandidates.FirstOrDefault(File.Exists);
        if (backendExecutable is not null)
        {
            return new BackendLaunchPlan(
                backendExecutable,
                [],
                Path.GetDirectoryName(backendExecutable)!,
                Path.GetFileName(backendExecutable));
        }

        var configuredPython = Environment.GetEnvironmentVariable("CSI_OPENBASE_PYTHON");
        if (!string.IsNullOrWhiteSpace(configuredPython))
        {
            var python = configuredPython.Trim();
            return new BackendLaunchPlan(
                python,
                ["-m", "scripts.run_openbase"],
                applicationDirectory,
                $"{Path.GetFileName(python)} -m scripts.run_openbase");
        }

        var developmentEntryPoint = FindDevelopmentEntryPoint();
        if (developmentEntryPoint is not null)
        {
            var script = developmentEntryPoint;
            var pythonProjectRoot = Directory.GetParent(Path.GetDirectoryName(script)!)!.FullName;
            var python = FindPython(pythonProjectRoot);
            return new BackendLaunchPlan(
                python,
                [script],
                pythonProjectRoot,
                $"{Path.GetFileName(python)} scripts/{Path.GetFileName(script)}");
        }

        throw new FileNotFoundException(
            "未找到 CSI OpenBase 后端。安装版需要 backend\\CSI.OpenBase.Backend.exe；" +
            "开发版请保留相邻的 python 项目，或设置 CSI_OPENBASE_BACKEND / CSI_OPENBASE_PYTHON。");
    }

    public async Task<Uri> StartAsync(
        BackendLaunchPlan plan,
        string workspaceDirectory,
        IProgress<string> progress,
        CancellationToken cancellationToken)
    {
        await StopAsync();

        Directory.CreateDirectory(workspaceDirectory);
        var port = ReserveLoopbackPort();
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var instanceNonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _desktopToken = token;
        _instanceNonce = instanceNonce;
        BaseAddress = new Uri($"http://127.0.0.1:{port}/");
        _lifetimeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var startInfo = new ProcessStartInfo
        {
            FileName = plan.FileName,
            WorkingDirectory = plan.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in plan.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["CSI_OPENBASE_HOME"] = Path.GetFullPath(workspaceDirectory);
        startInfo.Environment.Remove("CSI_OPENBASE_SESSION_HOME");
        startInfo.Environment["CSI_OPENBASE_HOST"] = "127.0.0.1";
        startInfo.Environment["CSI_OPENBASE_PORT"] = port.ToString();
        startInfo.Environment["CSI_OPENBASE_DESKTOP_TOKEN"] = token;
        startInfo.Environment["CSI_OPENBASE_INSTANCE_NONCE"] = instanceNonce;
        startInfo.Environment["CSI_OPENBASE_SESSION_SECRET"] = token;
        startInfo.Environment["PYTHONUTF8"] = "1";
        startInfo.Environment["PYTHONUNBUFFERED"] = "1";
        _log.Write("desktop", $"Launching {plan.DisplayName} on {BaseAddress} with data home {workspaceDirectory}");
        progress.Report("正在启动本地服务...");

        try
        {
            _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            _process.OutputDataReceived += (_, eventArgs) =>
            {
                if (eventArgs.Data is not null)
                {
                    _log.Write("backend", eventArgs.Data);
                }
            };
            _process.ErrorDataReceived += (_, eventArgs) =>
            {
                if (eventArgs.Data is not null)
                {
                    _log.Write("backend", eventArgs.Data);
                }
            };

            if (!_process.Start())
            {
                throw new InvalidOperationException("操作系统未能启动后端进程。");
            }

            _processJob.Assign(_process);
            _process.StandardInput.Close();
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
        }
        catch (Exception exception)
        {
            _log.Write("desktop", $"Backend process start failed: {exception}");
            var startupException = new InvalidOperationException(
                $"无法启动后端“{plan.DisplayName}”。请确认后端文件和 Python 环境完整。{Environment.NewLine}{exception.Message}",
                exception);
            await StopAsync();
            throw startupException;
        }

        try
        {
            return await WaitUntilHealthyAsync(progress, _lifetimeCancellation.Token);
        }
        catch
        {
            await StopAsync();
            throw;
        }
    }

    public async Task StopAsync()
    {
        _lifetimeCancellation?.Cancel();
        _lifetimeCancellation?.Dispose();
        _lifetimeCancellation = null;

        var process = _process;
        var baseAddress = BaseAddress;
        var desktopToken = _desktopToken;

        if (process is null)
        {
            ClearBackendState();
            return;
        }

        try
        {
            if (IsProcessRunning(process))
            {
                _log.Write("desktop", "Stopping backend process");
                var shutdown = await RequestGracefulShutdownAsync(baseAddress, desktopToken);
                if (shutdown.ForceRequired && IsProcessRunning(process))
                {
                    _log.Write("desktop", "Backend reported an active browser job; terminating its process tree");
                    process.Kill(entireProcessTree: true);
                    using var forcedJobTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await process.WaitForExitAsync(forcedJobTimeout.Token);
                }

                if (shutdown.Accepted && IsProcessRunning(process))
                {
                    using var apiShutdownTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    try
                    {
                        await process.WaitForExitAsync(apiShutdownTimeout.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        _log.Write("desktop", "Backend accepted shutdown but did not exit before timeout");
                    }
                }

                if (IsProcessRunning(process))
                {
                    process.CloseMainWindow();
                    using var windowCloseTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    try
                    {
                        await process.WaitForExitAsync(windowCloseTimeout.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        // A hidden console backend normally has no main window.
                    }
                }

                if (IsProcessRunning(process))
                {
                    _log.Write("desktop", "Backend did not exit gracefully; terminating its process tree");
                    process.Kill(entireProcessTree: true);
                    using var forcedTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await process.WaitForExitAsync(forcedTimeout.Token);
                }

                if (IsProcessRunning(process))
                {
                    throw new TimeoutException("后端进程在强制终止后仍未退出。");
                }
            }
        }
        catch (Exception exception)
        {
            if (!IsProcessRunning(process))
            {
                ReleaseStoppedProcess(process);
                return;
            }

            _log.Write("desktop", $"Backend shutdown failed: {exception}");
            throw new InvalidOperationException(
                "无法停止旧的本地服务，已取消重新启动。请退出应用后重试。",
                exception);
        }

        ReleaseStoppedProcess(process);
    }

    private static bool IsProcessRunning(Process process)
    {
        try
        {
            return !process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private void ReleaseStoppedProcess(Process process)
    {
        if (ReferenceEquals(_process, process))
        {
            _process = null;
            ClearBackendState();
        }
        process.Dispose();
    }

    private void ClearBackendState()
    {
        BaseAddress = null;
        _desktopToken = null;
        _instanceNonce = null;
    }

    private async Task<ShutdownResult> RequestGracefulShutdownAsync(Uri? baseAddress, string? token)
    {
        if (baseAddress is null || string.IsNullOrEmpty(token))
        {
            return new ShutdownResult(false, false);
        }

        try
        {
            var path = "api/shutdown";
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var request = new HttpRequestMessage(
                HttpMethod.Post, new Uri(baseAddress, path));
            request.Headers.TryAddWithoutValidation("X-CSI-Desktop-Token", token);
            using var response = await _httpClient.SendAsync(request, timeout.Token);
            if (response.IsSuccessStatusCode)
            {
                var responseBody = await response.Content.ReadAsStringAsync(timeout.Token);
                var forceRequired = false;
                try
                {
                    using var payload = JsonDocument.Parse(responseBody);
                    forceRequired = payload.RootElement.TryGetProperty("force_required", out var value) &&
                        value.ValueKind == JsonValueKind.True;
                }
                catch (JsonException)
                {
                    _log.Write("desktop", "Backend shutdown response was not valid JSON");
                }
                _log.Write("desktop", "Backend accepted graceful shutdown request");
                return new ShutdownResult(true, forceRequired);
            }

            _log.Write(
                "desktop",
                $"Backend shutdown endpoint returned HTTP {(int)response.StatusCode}");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            _log.Write("desktop", $"Backend shutdown endpoint was unavailable: {exception.Message}");
        }

        return new ShutdownResult(false, false);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await StopAsync();
        }
        catch (Exception exception)
        {
            _log.Write(
                "desktop",
                $"Backend shutdown failed during application exit; closing the process job: {exception}");
        }
        finally
        {
            try
            {
                _processJob.Dispose();
            }
            finally
            {
                _process?.Dispose();
                _process = null;
                ClearBackendState();
                _httpClient.Dispose();
            }
        }
    }

    private async Task<Uri> WaitUntilHealthyAsync(
        IProgress<string> progress,
        CancellationToken cancellationToken)
    {
        if (_process is null || BaseAddress is null ||
            string.IsNullOrEmpty(_desktopToken) || string.IsNullOrEmpty(_instanceNonce))
        {
            throw new InvalidOperationException("后端尚未启动。");
        }

        var healthUri = new Uri(BaseAddress, "health");
        var deadline = DateTimeOffset.UtcNow.AddSeconds(60);
        HttpStatusCode? lastStatus = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_process.HasExited)
            {
                await Task.Delay(100, cancellationToken);
                var details = BuildFailureDetails();
                throw new InvalidOperationException(
                    $"后端启动后立即退出（代码 {_process.ExitCode}）。{details}");
            }

            try
            {
                using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);
                requestTimeout.CancelAfter(TimeSpan.FromSeconds(2));
                using var request = new HttpRequestMessage(HttpMethod.Get, healthUri);
                request.Headers.TryAddWithoutValidation(
                    "X-CSI-Desktop-Token", _desktopToken);
                using var response = await _httpClient.SendAsync(
                    request, requestTimeout.Token);
                lastStatus = response.StatusCode;
                if (response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(
                        requestTimeout.Token);
                    try
                    {
                        using var payload = JsonDocument.Parse(body);
                        var root = payload.RootElement;
                        var statusMatches = root.TryGetProperty("status", out var status) &&
                            status.GetString() == "ok";
                        var modeMatches = root.TryGetProperty("mode", out var mode) &&
                            mode.GetString() == "local-archive";
                        var nonceMatches = root.TryGetProperty("instance_nonce", out var nonce) &&
                            nonce.GetString() == _instanceNonce;
                        if (statusMatches && modeMatches && nonceMatches)
                        {
                            _log.Write("desktop", "Backend health check passed");
                            return BaseAddress;
                        }
                    }
                    catch (JsonException)
                    {
                        // A different process may have claimed the reserved port.
                    }
                }

                if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
                {
                    progress.Report("本地服务已启动，正在等待后端就绪...");
                }
            }
            catch (HttpRequestException)
            {
                // The listener is not ready yet.
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // One health request timed out; keep waiting until the overall deadline.
            }

            await Task.Delay(750, cancellationToken);
        }

        if (lastStatus == HttpStatusCode.ServiceUnavailable)
        {
            throw new InvalidOperationException(
                "本地服务已启动，但健康检查持续返回不可用（HTTP 503）。请打开日志查看后端诊断信息。");
        }

        throw new TimeoutException(
            "等待本地服务响应超时。请确认 Python 依赖已安装，并打开日志查看后端错误。");
    }

    private string BuildFailureDetails()
    {
        var recent = _log.RecentText();
        if (recent.Contains("No module named", StringComparison.OrdinalIgnoreCase))
        {
            return " Python 依赖不完整，请先按源码安装说明安装项目依赖。详情见日志。";
        }

        if (recent.Contains("no active workspace", StringComparison.OrdinalIgnoreCase))
        {
            return " 所选目录还没有活动工作区，请先创建工作区或选择已有的 OpenBase 数据目录。";
        }

        return " 请打开日志查看完整错误。";
    }

    private static int ReserveLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static string? FindDevelopmentEntryPoint()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; directory is not null && depth < 10; depth++, directory = directory.Parent)
        {
            var entryPoint = Path.Combine(
                directory.FullName,
                "python",
                "scripts",
                "run_openbase.py");
            if (File.Exists(entryPoint))
            {
                return entryPoint;
            }
        }

        return null;
    }

    private static string FindPython(string pythonProjectRoot)
    {
        var localCandidates = new[]
        {
            Path.Combine(pythonProjectRoot, ".venv", "Scripts", "python.exe"),
            Path.Combine(pythonProjectRoot, "venv", "Scripts", "python.exe"),
        };
        var localPython = localCandidates.FirstOrDefault(File.Exists);
        return localPython ?? "python";
    }
}
