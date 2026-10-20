#!/usr/bin/env python3
"""Build, verify, test, and package the crash-report add-on for the supplied ZIP."""
import argparse
import hashlib
import os
from pathlib import Path
import shutil
import subprocess
import zipfile

ROOT = Path(__file__).resolve().parents[1]
ORIGINAL_HASHES = {
    "Gorilla Mod Manager 26.exe": "f09f6ff9d8da90978a747cd39ed8d5445607478419f4af8c9b65ce3db2dc684b",
    "GorillaReader.dll": "f961a7013428c0f6741301ff6cc82af17d809865fca473e83a8811bb35800365",
    "GorillaReader.runtimeconfig.json": "c230a317a54dd960bcbeb5f347f52e18dc665a26f7efda2159fced9a5ac7e097",
    "README.txt": "10ec3c936b7aba64e8aeb6f458d134b1a6c721c226126baf31d74a0e6ad04b66",
}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("input_zip", type=Path)
    parser.add_argument("--dotnet", default="dotnet", help=".NET 8 SDK host executable")
    args = parser.parse_args()
    source_zip = args.input_zip.resolve()
    dotnet = shutil.which(args.dotnet)
    if not dotnet:
        raise SystemExit("Install the .NET 8 SDK, or provide --dotnet /path/to/dotnet.")
    env = os.environ.copy()
    env["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1"
    env.setdefault("DOTNET_ROOT", str(Path(dotnet).resolve().parent))
    logs = []

    def run(*command):
        result = subprocess.run([str(x) for x in command], cwd=ROOT, env=env,
                                stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
        print(result.stdout, end="", flush=True)
        logs.append(result.stdout)
        if result.returncode:
            raise SystemExit(result.returncode)

    original = ROOT / "build/original"
    original.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(source_zip) as archive:
        names = archive.namelist()
        if sorted(names) != sorted(ORIGINAL_HASHES):
            raise SystemExit("Expected exactly the four files from the supplied v2.0.4 ZIP.")
        # Validate all four before writing any; never extract archive paths.
        files = {}
        for name, expected in ORIGINAL_HASHES.items():
            if archive.getinfo(name).file_size > 16 * 1024 * 1024:
                raise SystemExit("Unexpected input size: " + name)
            data = archive.read(name)
            if hashlib.sha256(data).hexdigest() != expected:
                raise SystemExit("Original SHA-256 differs: " + name)
            files[name] = data
        for name, data in files.items():
            (original / name).write_bytes(data)

    projects = ["src/CrashDetails/CrashDetails.csproj", "src/CakInspectorApp/CakInspectorApp.csproj",
                "src/Patcher/Patcher.csproj", "tests/CrashDetails.Tests.csproj"]
    run(dotnet, "tool", "restore")
    for project in projects:
        run(dotnet, "restore", project, "--locked-mode")
        run(dotnet, "build", project, "--configuration", "Release", "--no-restore")
    run(dotnet, ROOT / "tests/bin/Release/net8.0/CrashDetails.Tests.dll")

    output = ROOT / "artifacts/Gorilla Mod Manager 26 - Crash and CAK Checks"
    output.mkdir(parents=True, exist_ok=True)
    add_on = ROOT / "src/CrashDetails/bin/Release/net48/GorillaCrashDetails.dll"
    patched = output / "Gorilla Mod Manager 26.exe"
    run(dotnet, ROOT / "src/Patcher/bin/Release/net8.0/Patcher.dll",
        original / "Gorilla Mod Manager 26.exe", add_on, patched)
    for name in ("GorillaReader.dll", "GorillaReader.runtimeconfig.json"):
        shutil.copyfile(original / name, output / name)
    shutil.copyfile(add_on, output / add_on.name)
    inspector = ROOT / "src/CakInspectorApp/bin/Release/net48/Gorilla CAK Inspector.exe"
    shutil.copyfile(inspector, output / inspector.name)
    inspector_config = inspector.with_suffix(".exe.config")
    if inspector_config.exists():
        shutil.copyfile(inspector_config, output / inspector_config.name)
    shutil.copyfile(original / "README.txt", output / "README.original.txt")
    shutil.copyfile(ROOT / "docs/CRASH-DETAILS.txt", output / "README.txt")
    shutil.copyfile(ROOT / "docs/THIRD-PARTY-NOTICES.txt", output / "THIRD-PARTY-NOTICES.txt")

    # ILVerify resolves references by their assembly names. Use a temporary
    # alias for the original's GorillaModManager identity; ship its normal name.
    verify = ROOT / "build/verification"
    verify.mkdir(parents=True, exist_ok=True)
    alias = verify / "GorillaModManager.dll"
    shutil.copyfile(patched, alias)
    nuget = Path(env.get("NUGET_PACKAGES", Path.home() / ".nuget/packages"))
    references = sorted((nuget / "microsoft.netframework.referenceassemblies.net48/1.0.3").rglob("*.dll"))
    if not references:
        raise SystemExit(".NET Framework reference assemblies not found after restore.")
    reference_args = []
    for reference in references + [add_on, alias, inspector]:
        reference_args.extend(["-r", reference])
    run(dotnet, "tool", "run", "ilverify", str(alias), "--include", "Doctor.*Examine", "--statistics", *reference_args)
    run(dotnet, "tool", "run", "ilverify", str(add_on), "--statistics", *reference_args)
    run(dotnet, "tool", "run", "ilverify", str(inspector), "--statistics", *reference_args)

    validation = ("VALIDATION OF THIS BUILD\n"
                  "Build/diagnostic checks follow. These are not Windows UI or WWE game tests.\n\n" + "\n".join(logs))
    (output / "VALIDATION.txt").write_text(validation, encoding="utf-8")
    hashes = {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(output.iterdir()) if p.is_file() and p.name != "SHA256SUMS.txt"}
    (output / "SHA256SUMS.txt").write_text("".join(f"{digest}  {name}\n" for name, digest in hashes.items()), encoding="utf-8")

    release_zip = ROOT / "artifacts/Gorilla-Mod-Manager-26-v2.0.4-Crash-and-CAK-Checks.zip"
    with zipfile.ZipFile(release_zip, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        for name in list(hashes) + ["SHA256SUMS.txt"]:
            archive.write(output / name, name)
    with zipfile.ZipFile(release_zip) as archive:
        if archive.testzip() is not None:
            raise SystemExit("Output ZIP integrity check failed.")
        for name, expected in hashes.items():
            if hashlib.sha256(archive.read(name)).hexdigest() != expected:
                raise SystemExit("Packaged hash mismatch: " + name)

    source_output = ROOT / "artifacts/Gorilla-Crash-and-CAK-Checks-Source.zip"
    with zipfile.ZipFile(source_output, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        paths = [ROOT / x for x in ("README.md", ".gitignore", "global.json", "Directory.Build.props", ".config/dotnet-tools.json")]
        for folder in ("src", "tests", "tools", "docs"):
            paths.extend(p for p in (ROOT / folder).rglob("*") if p.is_file() and not {"bin", "obj", "__pycache__"}.intersection(p.relative_to(ROOT).parts))
        for path in sorted(paths):
            archive.write(path, str(path.relative_to(ROOT)))
        archive.write(source_zip, "original-upload.zip")
    with zipfile.ZipFile(source_output) as archive:
        if archive.testzip() is not None:
            raise SystemExit("Source ZIP integrity check failed.")
    print("Created:", release_zip)
    print("Created:", source_output)


if __name__ == "__main__":
    main()
