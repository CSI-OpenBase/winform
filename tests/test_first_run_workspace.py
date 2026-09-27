from __future__ import annotations

import unittest
from pathlib import Path


PROJECT_ROOT = Path(__file__).resolve().parents[1]
SETTINGS_SOURCE = (
    PROJECT_ROOT / "CSI.OpenBase.Desktop" / "DesktopSettings.cs"
).read_text(encoding="utf-8")
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
    def test_settings_distinguish_a_suggestion_from_a_saved_workspace(self) -> None:
        self.assertIn(
            "internal bool HasPersistedWorkspace { get; private set; }",
            SETTINGS_SOURCE,
        )
        self.assertGreaterEqual(
            SETTINGS_SOURCE.count("HasPersistedWorkspace = true;"),
            2,
        )

    def test_first_launch_prompts_before_starting_the_backend(self) -> None:
        prompt = "await SelectWorkspaceAsync(restartBackend: false)"
        webview_initialization = "await CreateWebViewEnvironmentAsync"
        self.assertIn(prompt, MAIN_FORM_SOURCE)
        self.assertLess(
            MAIN_FORM_SOURCE.index(prompt),
            MAIN_FORM_SOURCE.index(webview_initialization),
        )
        self.assertIn(
            "if (!_settings.HasPersistedWorkspace)",
            MAIN_FORM_SOURCE,
        )

    def test_restart_stays_disabled_until_a_workspace_is_saved(self) -> None:
        self.assertIn(
            "_restartButton.Enabled = enabled && _settings.HasPersistedWorkspace;",
            MAIN_FORM_SOURCE,
        )
        self.assertIn(
            "await SelectWorkspaceAsync(restartBackend: false);",
            MAIN_FORM_SOURCE,
        )
        self.assertIn(
            "if (!_settings.HasPersistedWorkspace)",
            MAIN_FORM_SOURCE,
        )

    def test_top_controls_share_a_single_toolbar(self) -> None:
        self.assertIn("var toolbar = new ToolStrip", MAIN_FORM_SOURCE)
        self.assertIn('CreateToolbarButton("目录设置"', MAIN_FORM_SOURCE)
        self.assertIn('CreateToolbarButton("打开日志"', MAIN_FORM_SOURCE)
        self.assertIn('CreateToolbarButton("任务"', MAIN_FORM_SOURCE)
        self.assertIn('CreateToolbarButton("关于"', MAIN_FORM_SOURCE)
        self.assertIn("toolbar.Items.AddRange", MAIN_FORM_SOURCE)
        self.assertIn("UpdateWorkspaceToolbarState();", MAIN_FORM_SOURCE)
        self.assertNotIn("_workspaceTextBox", MAIN_FORM_SOURCE)
        self.assertNotIn("workspaceRow", MAIN_FORM_SOURCE)

    def test_workspace_state_is_rechecked_after_webview_initialization(self) -> None:
        webview_ready = "_webViewReady = true;"
        persisted_workspace_check = "if (!_settings.HasPersistedWorkspace)"
        ready_index = MAIN_FORM_SOURCE.index(webview_ready)
        post_ready_check_index = MAIN_FORM_SOURCE.find(
            persisted_workspace_check,
            ready_index,
        )
        backend_start_index = MAIN_FORM_SOURCE.find(
            "await RestartBackendAsync();",
            ready_index,
        )
        self.assertNotEqual(post_ready_check_index, -1)
        self.assertLess(
            post_ready_check_index,
            backend_start_index,
        )
        self.assertNotIn("var workspaceConfigured", MAIN_FORM_SOURCE)

    def test_task_panel_mirrors_the_authenticated_local_page(self) -> None:
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
        self.assertIn("IsCurrentBackendSource(eventArgs.Source)", MAIN_FORM_SOURCE)
        self.assertNotIn('new Uri(address, "api/state")', MAIN_FORM_SOURCE)

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


if __name__ == "__main__":
    unittest.main()
