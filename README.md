# Gorilla Mod Manager: game crash explanations and CAK checks

Adds clearer WWE 2K26 crash explanations to the supplied Gorilla Mod Manager 26
v2.0.4 while retaining the original features and controls. This is an unofficial
add-on to the uploaded binary, not a replacement mod manager or an official release.

## Downloads paused: antivirus report under investigation

Microsoft Defender reported **Trojan:Win32/Ulthar.A!ml** for the bundled
**Gorilla CAK Inspector.exe**. The downloadable application and binary-containing
source ZIP have been withdrawn from the current branch pending investigation.
Do not run or allow the previously downloaded build, add antivirus exclusions,
or obtain it from an older commit as a workaround.

The 54 automated checks were functional and structural checks, **not an antivirus
scan or evidence that the package is malware-free**. No clean replacement has
been validated. Source code remains available for review. See
[the investigation record](docs/ANTIVIRUS-REPORT.md) for hashes, established facts
and current limits.

The original Windows app targets .NET Framework 4.8. Its `GorillaReader.dll`
helper targets .NET 10. The upload contains these compiled files and a README;
it does not contain the original source project.

On **Fix My Game**, the added card shows the most recent Windows crash/hang,
component, exception code, offset, confidence and suggested checks. It saves a
local text report with an **Open folder** link. It explains when evidence is
missing and avoids treating a faulting plugin, a shared Windows DLL or one
successful test as proof of a root cause. Existing suspects, backups, restore
controls, mod management, TRIBUTE, Discovery and update code are retained.

See [the package instructions](docs/CRASH-DETAILS.txt) for Windows use and limits.
The report may contain personal paths; review it before sharing. No report is
uploaded and the add-on does not change game or mod files.

**Fix My Game > Check Again** also reads enabled CAK headers and supported
plaintext file/directory tables, checks entry/payload bounds, and reports shared
asset IDs across active archives. File-specific CakeHook messages are quoted
when available, with stale logs identified. The included **Gorilla CAK Inspector**
Windows tool lets you choose or drop a CAK before installing it and save a report.

Supported plaintext FDIR versions are 6.7, 6.8, 8.1, 8.2, 8.3, 8.7, 9.1, 9.2 and
9.3. Encrypted metadata and unknown versions, including 9.9, are explicitly marked
as limited inspection. The tool does not validate checksums, decompress assets or
establish game compatibility. Shared asset IDs may be intentional overrides and
are not proof of a crash cause. No user CAK has been supplied for this task.

CAK format research credit: [Nenkai/Bakery](https://github.com/Nenkai/Bakery),
revision `f704d3724d713f36cff74a82e7466d5710b510de`. See
[the acknowledgment and MIT notice](docs/THIRD-PARTY-NOTICES.txt).

## Build and validate

Install Python 3.11+ and the .NET 8 SDK. From this checkout run:

```sh
python tools/build.py "/path/to/Gorilla Mod Manager 26 v2.0.4.zip"
```

Use `--dotnet /path/to/dotnet` when the SDK is outside PATH. A local copy of the
original user-supplied ZIP is required; binary-containing archives are currently
withdrawn. Builds are for investigation, not cleared releases. NuGet access is
needed on the first build. Dependencies and the ILVerify
tool are pinned; NuGet lock files are enforced. TLS, NuGet verification and the
original binary hashes are preserved.

The script checks the input hashes, builds the add-on, CAK Inspector and patcher, runs the
diagnostic/integration tests, and patches only the original `Doctor.Examine`
return paths. It verifies that the other 3,955 method bodies, all original
resources and assembly identity remain unchanged. ILVerify checks the modified
method and every method in the add-on and CAK Inspector against .NET Framework
4.8 references.

The tests use actual model types from the uploaded executable, including
preservation of existing fix delegates and suspects, local report output, missing
or historical evidence, unavailable logs and report-write failures. Synthetic CAK
fixtures exercise supported layouts, structural failures, bounded inspection,
asset overlaps, unchanged input bytes and exact-filename loader-log matching.

Generated packages, checksum manifests and validation output are in `artifacts/`.
Original inputs and temporary reference aliases remain in ignored `build/` paths.
The original uploaded executable is never overwritten.

## Windows validation still required

This Linux environment cannot run the Windows desktop interfaces or WWE 2K26. Build,
IL and diagnostic checks passed here; actual Windows startup, the new card's
layout, CAK Inspector window/drag-and-drop, Event Viewer access and a real
game-crash workflow remain untested.

Windows runtime validation must wait until the antivirus detection has been
resolved through a suitable security review. Do not restore or allow the flagged
build to carry out the previously described UI/game tests.

This patch accepts only the exact uploaded v2.0.4 executable. Later official
updates may replace its report hook and require a separately reviewed patch.
