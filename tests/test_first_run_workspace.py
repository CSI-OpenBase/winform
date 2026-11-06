from __future__ import annotations

import subprocess
import tempfile
import unittest
from pathlib import Path


PROJECT_ROOT = Path(__file__).resolve().parents[1]
MAIN_FORM_SOURCE = (
    PROJECT_ROOT / "CSI.OpenBase.Desktop" / "MainForm.cs"
).read_text(encoding="utf-8")
TASK_PANEL_SOURCE = (
    PROJECT_ROOT / "CSI.OpenBase.Desktop" / "TaskPanelControl.cs"
).read_text(encoding="utf-8")
APP_LOG_SOURCE = (
    PROJECT_ROOT / "CSI.OpenBase.Desktop" / "AppLog.cs"
).read_text(encoding="utf-8")
BEIJING_TIME_SOURCE = (
    PROJECT_ROOT / "CSI.OpenBase.Desktop" / "BeijingTime.cs"
).read_text(encoding="utf-8")
PROJECT_SOURCE = (
    PROJECT_ROOT / "CSI.OpenBase.Desktop" / "CSI.OpenBase.Desktop.csproj"
).read_text(encoding="utf-8")


class FirstRunWorkspaceContractTests(unittest.TestCase):
    def test_startup_only_opens_the_folder_picker_on_user_request(self) -> None:
        startup = MAIN_FORM_SOURCE.split(
            "private async Task InitializeAndStartAsync()", 1
        )[1].split("private async Task<bool> SelectWorkspaceAsync()", 1)[0]
        self.assertNotIn("SelectWorkspaceAsync(", startup)
        self.assertNotIn("ShowDialog(", startup)
        self.assertIn("await RestartBackendAsync();", startup)
        self.assertIn(
            "_browseMenuItem.Click += async (_, _) => await SelectWorkspaceAsync();",
            MAIN_FORM_SOURCE,
        )

    def test_workspace_is_initialized_before_starting_the_backend(self) -> None:
        restart = MAIN_FORM_SOURCE.split("private async Task RestartBackendAsync()", 1)[1]
        self.assertLess(
            restart.index("_settings.EnsureWorkspace();"),
            restart.index("await _backend.StartAsync("),
        )
        self.assertIn('ResetTaskPanel("工作目录不可用");', restart)
        self.assertIn(
            "_restartMenuItem.Enabled = enabled && _settings.HasPersistedWorkspace;",
            MAIN_FORM_SOURCE,
        )

    def test_top_controls_share_the_main_menu(self) -> None:
        self.assertIn("var menuStrip = new MenuStrip", MAIN_FORM_SOURCE)
        self.assertIn('new ToolStripMenuItem("目录设置")', MAIN_FORM_SOURCE)
        self.assertIn('new ToolStripMenuItem("运行日志")', MAIN_FORM_SOURCE)
        self.assertIn('new ToolStripMenuItem("任务")', MAIN_FORM_SOURCE)
        self.assertIn('new ToolStripMenuItem("关于")', MAIN_FORM_SOURCE)
        self.assertIn("MainMenuStrip = menuStrip;", MAIN_FORM_SOURCE)
        self.assertIn("UpdateWorkspaceMenuState();", MAIN_FORM_SOURCE)
        self.assertNotIn("_workspaceTextBox", MAIN_FORM_SOURCE)
        self.assertNotIn("workspaceRow", MAIN_FORM_SOURCE)

    def test_task_panel_mirrors_the_authenticated_local_page(self) -> None:
        self.assertIn("private bool _taskPanelVisible = true;", MAIN_FORM_SOURCE)
        self.assertIn(
            "AddScriptToExecuteOnDocumentCreatedAsync",
            MAIN_FORM_SOURCE,
        )
        self.assertIn(
            "document.querySelector('.history-band tbody')",
            MAIN_FORM_SOURCE,
        )
        self.assertIn("WebMessageReceived", MAIN_FORM_SOURCE)
        self.assertIn("state.jobs.slice(0, 100)", MAIN_FORM_SOURCE)
        self.assertIn("message: clean(job.message, 500)", MAIN_FORM_SOURCE)
        self.assertIn("window.__csiDesktopTaskState = projected", MAIN_FORM_SOURCE)
        self.assertIn(
            "if (window.__csiDesktopTaskState) return window.__csiDesktopTaskState",
            MAIN_FORM_SOURCE,
        )
        self.assertIn("IsCurrentBackendSource(eventArgs.Source)", MAIN_FORM_SOURCE)
        self.assertNotIn('new Uri(address, "api/state")', MAIN_FORM_SOURCE)

    def test_video_tasks_open_the_corresponding_local_archive(self) -> None:
        self.assertIn("event EventHandler<TaskVideoRequestedEventArgs>? VideoRequested", TASK_PANEL_SOURCE)
        self.assertIn("Keys.Enter or Keys.Space", TASK_PANEL_SOURCE)
        self.assertIn("AttachActivationOnClick(body)", TASK_PANEL_SOURCE)
        self.assertIn("NavigateToVideo(eventArgs.VideoId)", MAIN_FORM_SOURCE)
        self.assertIn('$"videos/{videoId}"', MAIN_FORM_SOURCE)

    def test_comment_export_directory_uses_native_folder_picker(self) -> None:
        self.assertIn('case "select-comment-export-directory":', MAIN_FORM_SOURCE)
        self.assertIn('Description = "选择评论导出目录"', MAIN_FORM_SOURCE)
        self.assertIn('type = "comment-export-directory-selected"', MAIN_FORM_SOURCE)
        self.assertIn("PostWebMessageAsJson", MAIN_FORM_SOURCE)

    def test_task_panel_does_not_reload_or_poll_independently(self) -> None:
        self.assertNotIn("CoreWebView2.Reload()", MAIN_FORM_SOURCE)
        self.assertNotIn("__csiDesktopRefreshTasks", MAIN_FORM_SOURCE)
        self.assertNotIn("originalFetch('/api/state'", MAIN_FORM_SOURCE)
        self.assertNotIn("RefreshRequested", TASK_PANEL_SOURCE)

    def test_task_panel_supports_per_monitor_dpi_and_keyboard_navigation(self) -> None:
        self.assertIn(
            "<ApplicationHighDpiMode>PerMonitorV2</ApplicationHighDpiMode>",
            PROJECT_SOURCE,
        )
        self.assertIn("AutoScaleMode = AutoScaleMode.Dpi", MAIN_FORM_SOURCE)
        self.assertIn("SetStyle(ControlStyles.Selectable, true)", TASK_PANEL_SOURCE)
        self.assertIn("protected override bool IsInputKey", TASK_PANEL_SOURCE)
        self.assertIn("OnDpiChangedAfterParent", TASK_PANEL_SOURCE)

    def test_task_panel_follows_backend_connection_lifecycle(self) -> None:
        prepare_index = MAIN_FORM_SOURCE.index("PrepareTaskPanelForConnection();")
        start_index = MAIN_FORM_SOURCE.index("await _backend.StartAsync(", prepare_index)
        navigation_index = MAIN_FORM_SOURCE.index("_webView.Source = address;", start_index)
        self.assertLess(prepare_index, start_index)
        self.assertLess(start_index, navigation_index)
        self.assertIn('ResetTaskPanel("本地服务已断开");', MAIN_FORM_SOURCE)
        self.assertIn('ResetTaskPanel("应用正在关闭");', MAIN_FORM_SOURCE)

    def test_task_panel_has_loading_empty_error_and_disconnected_states(self) -> None:
        for state_method in (
            "ShowLoading",
            "ShowDisconnected",
            "ShowError",
            "SetState",
        ):
            with self.subTest(state_method=state_method):
                self.assertIn(f"void {state_method}", TASK_PANEL_SOURCE)
        self.assertIn('SetPlaceholder("暂无任务记录")', TASK_PANEL_SOURCE)

    def test_desktop_times_are_fixed_to_beijing_instead_of_system_local_time(self) -> None:
        self.assertIn("TimeSpan.FromHours(8)", BEIJING_TIME_SOURCE)
        self.assertIn("DateTimeOffset.UtcNow.ToOffset(Offset)", BEIJING_TIME_SOURCE)
        self.assertIn("BeijingTime.Now", TASK_PANEL_SOURCE)
        self.assertIn("BeijingTime.Convert(parsed)", TASK_PANEL_SOURCE)
        self.assertNotIn("ToLocalTime()", TASK_PANEL_SOURCE)
        self.assertGreaterEqual(APP_LOG_SOURCE.count("BeijingTime.Now"), 2)


class DesktopSettingsBehaviorTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.artifacts = tempfile.TemporaryDirectory(prefix="openbase-settings-tests-")
        cls.addClassCleanup(cls.artifacts.cleanup)
        built = subprocess.run(
            [
                "dotnet", "build",
                str(PROJECT_ROOT / "tests" / "SettingsHarness" / "SettingsHarness.csproj"),
                "--artifacts-path", cls.artifacts.name,
                "-c", "Release", "--nologo",
            ],
            capture_output=True, text=True, encoding="utf-8", errors="replace",
            timeout=120,
        )
        if built.returncode:
            raise AssertionError(built.stdout + built.stderr)
        cls.harness = (
            Path(cls.artifacts.name) / "bin" / "SettingsHarness" / "release"
            / "SettingsHarness.dll"
        )

    def check_scenario(self, scenario: str) -> None:
        with tempfile.TemporaryDirectory(prefix="openbase-workspace-") as workspace:
            result = subprocess.run(
                ["dotnet", str(self.harness), scenario, workspace],
                capture_output=True, text=True, encoding="utf-8", errors="replace",
                timeout=30,
            )
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)

    def test_unsaved_workspace_is_created_persisted_and_reused(self) -> None:
        self.check_scenario("first-run")

    def test_saved_workspace_and_existing_data_are_preserved(self) -> None:
        self.check_scenario("saved-workspace")

    def test_user_can_change_workspace_without_moving_old_data(self) -> None:
        self.check_scenario("change-workspace")

    def test_unavailable_workspace_does_not_save_a_bad_default(self) -> None:
        self.check_scenario("unavailable-workspace")

    def test_invalid_settings_recover_in_the_same_settings_file(self) -> None:
        self.check_scenario("invalid-settings")


if __name__ == "__main__":
    unittest.main()
