"""Offline Windows-host Phase 3 contract checks; runtime tests require Windows 11."""

from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[1]


class WindowsPhase3ContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.host = (ROOT / "src/windows/TabletDisplay.Host/AdbSession.cs").read_text()
        cls.window = (ROOT / "src/windows/TabletDisplay.Host/MainWindow.xaml.cs").read_text()

    def test_forward_is_explicitly_loopback_and_abstract_on_android(self):
        # Platform-Tools 37 rejects "tcp:localhost:0"; the bind is verified instead.
        self.assertIn('"forward", "tcp:0"', self.host)
        self.assertNotIn('"tcp:localhost:0"', self.host)
        self.assertIn("IsLoopbackOnly(port)", self.host)
        self.assertIn("IPAddress.IsLoopback", self.host)
        self.assertIn('localabstract:{AbstractSocket}', self.host)
        self.assertIn('ConnectAsync("127.0.0.1", _forwardPort', self.host)

    def test_token_is_sent_on_stdin_and_forward_is_removed_by_exact_port(self):
        self.assertIn('"shell", "-T", "run-as"', self.host)
        self.assertIn('"\'cat > files/session_token\'"', self.host)
        self.assertIn('cancellationToken, tokenHex)', self.host)
        self.assertIn('"forward", "--remove", $"tcp:{_forwardPort}"', self.host)

    def test_authorization_disconnect_and_liveness_states_exist(self):
        self.assertIn('fields[1] == "unauthorized"', self.host)
        self.assertIn('PeriodicTimer(TimeSpan.FromSeconds(2))', self.host)
        self.assertIn("WatchSessionAsync", self.window)
        self.assertIn("more than one", self.host.lower())

    def test_manual_windows_test_is_separately_documented(self):
        checklist = ROOT / "docs/testing/phase-3-windows-checklist.md"
        self.assertTrue(checklist.is_file())
        self.assertIn("NOT RUN", checklist.read_text())
        self.assertIn("Phase 2", checklist.read_text())
        self.assertIn("adb devices -l", checklist.read_text())


if __name__ == "__main__":
    unittest.main()
