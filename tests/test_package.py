import hashlib
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import warnings
import zipfile
from tools.verify_package import EXPECTED_FILES, verify_release


class ReleasePackageTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.path = Path(self.temp.name) / "release.zip"
        self.files = {name: b"fixture: " + name.encode() for name in EXPECTED_FILES if name != "SHA256SUMS.txt"}

    def tearDown(self):
        self.temp.cleanup()

    def write(self, extra=None, wrong_checksum=False, duplicate=False):
        manifest = "".join(hashlib.sha256(data).hexdigest() + "  " + name + "\n" for name, data in sorted(self.files.items()))
        if wrong_checksum:
            manifest = "0" * 64 + manifest[64:]
        with zipfile.ZipFile(self.path, "w") as archive:
            for name, data in self.files.items():
                archive.writestr(name, data)
            archive.writestr("SHA256SUMS.txt", manifest)
            if extra:
                archive.writestr(*extra)
            if duplicate:
                with warnings.catch_warnings():
                    warnings.simplefilter("ignore", UserWarning)
                    archive.writestr("README.txt", b"duplicate")

    def test_integrated_only_release_passes(self):
        self.write()
        self.assertEqual(verify_release(self.path)["files"], 10)

    def test_standalone_executable_cannot_return_as_stale_output(self):
        self.write(extra=("Gorilla CAK Inspector.exe", b"stale output"))
        with self.assertRaises(ValueError):
            verify_release(self.path)

    def test_another_extra_executable_is_rejected(self):
        self.write(extra=("Other.exe", b"unexpected executable"))
        with self.assertRaises(ValueError):
            verify_release(self.path)

    def test_blocked_content_is_rejected_even_with_a_different_name(self):
        self.write()
        blocked = hashlib.sha256(self.files["GorillaReader.dll"]).hexdigest()
        with patch("tools.verify_package.BLOCKED_SHA256", {blocked}):
            with self.assertRaisesRegex(ValueError, "previously flagged"):
                verify_release(self.path)

    def test_checksum_mismatch_is_rejected(self):
        self.write(wrong_checksum=True)
        with self.assertRaisesRegex(ValueError, "Checksum mismatch"):
            verify_release(self.path)

    def test_duplicate_entries_are_rejected(self):
        self.write(duplicate=True)
        with self.assertRaisesRegex(ValueError, "Duplicate"):
            verify_release(self.path)

    def test_missing_dependency_is_rejected(self):
        self.files.pop("MykelovvesDiagnostics.dll")
        self.write()
        with self.assertRaises(ValueError):
            verify_release(self.path)


if __name__ == "__main__":
    unittest.main()
