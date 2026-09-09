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


if __name__ == "__main__":
    unittest.main()
