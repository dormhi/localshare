"""Offline Android-side Phase 3 protocol and permission checks."""

from pathlib import Path
import unittest
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parents[1]


class AndroidPhase3ContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.android = (ROOT / "src/android/app/src/main/java/com/dormhi/localshare/MainActivity.kt").read_text()
        cls.manifest = ET.parse(ROOT / "src/android/app/src/main/AndroidManifest.xml").getroot()
        cls.gradle = (ROOT / "src/android/app/build.gradle.kts").read_text()

    def test_hello_header_is_fixed_size_and_versioned(self):
        # Golden wire vector: TDSH, v1, HELLO, payload length 32, 32-byte token.
        hello = b"TDSH\x01\x01\x00\x20" + bytes(32)
        self.assertEqual(len(hello), 40)
        self.assertEqual(hello[:8], bytes((84, 68, 83, 72, 1, 1, 0, 32)))
        self.assertIn("HANDSHAKE_SIZE = 40", self.android)
        self.assertIn("MessageDigest.isEqual", self.android)
        self.assertIn("readFully(request)", self.android)

    def test_android_uses_abstract_socket_without_network_permission(self):
        self.assertIn('LocalServerSocket(SOCKET_NAME)', self.android)
        self.assertIn('SOCKET_NAME = "com.dormhi.localshare.session"', self.android)
        permissions = [node for node in self.manifest if node.tag.endswith("uses-permission")]
        self.assertEqual(permissions, [])

    def test_debug_token_is_read_from_private_storage_then_deleted(self):
        self.assertIn('filesDir, "session_token"', self.android)
        self.assertIn('file.delete()', self.android)
        self.assertIn("run-as", (ROOT / "protocol/README.md").read_text())

    def test_selected_package_identity_matches_namespace(self):
        expected = "com.dormhi.localshare"
        self.assertIn(f'namespace = "{expected}"', self.gradle)
        self.assertIn(f'applicationId = "{expected}"', self.gradle)
        self.assertIn(expected, self.android)


if __name__ == "__main__":
    unittest.main()
