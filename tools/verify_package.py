"""Validate the exact integrated-only release contents and their checksums."""
import hashlib
from pathlib import PurePosixPath
import zipfile

EXPECTED_FILES = {
    "Mykelovves Mod Manager.exe", "MykelovvesDiagnostics.dll", "GorillaReader.dll",
    "GorillaReader.runtimeconfig.json", "README.original.txt", "README.txt",
    "THIRD-PARTY-NOTICES.txt", "ANTIVIRUS-STATUS.txt", "VALIDATION.txt", "SHA256SUMS.txt",
}
BLOCKED_SHA256 = {"c1ab7774bb5cce98c76e569f9e8bf437e1a45ece3cc6699429149a862f23c920"}


def verify_release(path):
    with zipfile.ZipFile(path) as archive:
        names = archive.namelist()
        if len(names) != len(set(names)):
            raise ValueError("Duplicate archive entries are not permitted.")
        if set(names) != EXPECTED_FILES:
            raise ValueError("Unexpected or missing release files: " + str(set(names) ^ EXPECTED_FILES))
        if any(PurePosixPath(name).name != name or "\\" in name for name in names):
            raise ValueError("Release files must be top-level files.")
        if archive.testzip() is not None:
            raise ValueError("ZIP integrity verification failed.")
        manifest = {}
        for line in archive.read("SHA256SUMS.txt").decode("utf-8").splitlines():
            digest, name = line.split("  ", 1)
            if name in manifest:
                raise ValueError("Duplicate checksum entry.")
            manifest[name] = digest
        if set(manifest) != EXPECTED_FILES - {"SHA256SUMS.txt"}:
            raise ValueError("Checksum manifest does not cover the exact release.")
        for name in names:
            digest = hashlib.sha256(archive.read(name)).hexdigest()
            if digest in BLOCKED_SHA256:
                raise ValueError("The previously flagged executable is forbidden in the release.")
            if name != "SHA256SUMS.txt" and manifest[name] != digest:
                raise ValueError("Checksum mismatch: " + name)
        return {"files": len(names), "standalone_inspector_present": False}
