# Mykelovves Mod Manager

Mykelovves Mod Manager r3 adds same-file comparisons and clearer crash reports
to the supplied Gorilla Mod Manager 26 v2.0.4. The window title, header, launch
executable and downloads use the requested name. Original mod management,
TRIBUTE, Discovery, backup, restore, mod search and update code are retained.
This is an unofficial modification; original author: Mr Creeper HD.

- [Download r3 application ZIP — antivirus clearance unverified](downloads/Mykelovves-Mod-Manager-r3.zip)
- [Download r3 code-only source ZIP](downloads/Mykelovves-Mod-Manager-r3-Source.zip)
- [Actual antivirus status](downloads/ANTIVIRUS-STATUS.txt)
- [Download checksums](downloads/SHA256SUMS.txt)

## Same files and crash investigation

Open **Mykelovves Mod Manager.exe**, then **Fix My Game > Check Again**.
The integrated checks read enabled, top-level `.cak` files in the configured
mods folder. They show:

- **Identical whole CAKs:** archive names and matching whole-file SHA-256 hashes.
- **Identical stored files inside CAKs:** the exact internal path, every owning
  archive and matching stored-payload hashes, including matching subsets of three
  or more archives.
- **Different stored versions:** the same internal path occurs in multiple CAKs
  with different payload hashes. Shared IDs with different paths are identified
  separately and are not called the same file.
- **File-specific problems:** invalid archive tables, references or payload
  bounds, naming the internal file when available, and recent CakeHook errors
  that mention the exact CAK filename.
- **Crash investigation leads:** ranked file problems, loader errors and
  differing files; a modification close to a recorded crash is a timing clue.
  Completed results from the original mod search name the archive isolated by
  the user's tests without inventing a particular internal culprit.

The Windows crash explanation shows the recorded component, exception code,
offset, time, confidence and suggested checks. A faulting DLL, duplicate file or
shared path alone cannot prove the root cause. The report says when the cause is
unknown and distinguishes confirmed file defects from their unconfirmed connection
to a crash. Different compression can produce different stored bytes for the same
decoded content; intentional replacements are common. The loader's chosen winner
and decoded asset validity are not determined here.

**Open folder** on the report cards leads to local text reports:
`Crash Reports/Latest crash report.txt` and `Crash Reports/Same files report.txt`
under the existing data folder. The full same-files report includes every group
retained within the scan limits, beyond the 12 visible shared-file cards. Reports
may contain personal paths; review them before sharing. No report is uploaded.
The add-on never extracts, edits, deletes or disables game/mod files.

Supported plaintext FDIR versions: 6.7, 6.8, 8.1, 8.2, 8.3, 8.7, 9.1, 9.2 and
9.3. Encrypted metadata and unsupported versions, including 9.9, are **limited**,
not declared corrupt. Encrypted payloads are compared as stored ciphertext only.
The tool does not decrypt, decompress or validate texture/model compatibility.

Scans are bounded: 256 CAKs, 32 MiB metadata and 100,000 entries per CAK;
200,000 retained entry references, 1,000 shared-file groups, 20 file-specific
cards and 12 shared-file cards. Hash reads use at most 256 MiB and 15 seconds,
with 64 MiB per whole archive and 32 MiB per payload. Files changed during a
scan, unreadable files and results beyond these limits remain explicitly
unverified. An absent comparison does not mean files differ or are healthy.

See [Windows instructions and limits](docs/CRASH-DETAILS.txt).
No user CAK has been supplied or diagnosed during this task.

## Antivirus status

Microsoft Defender flagged the earlier standalone **Gorilla CAK Inspector.exe**
as **Trojan:Win32/Ulthar.A!ml**. That entire standalone program and its project
were removed in r2 and remain excluded from r3. It has not been renamed or
reintroduced. The original flagged download remains withdrawn. The r3 name
change is the user's requested branding of the main manager.

Removing that component does **not** establish that this package is safe.
Microsoft Defender is unavailable in this Linux environment. A verified ClamAV
engine was obtained, but the proxy blocked official virus-definition downloads,
so no malware scan completed. Functional tests are not antivirus scans. Keep
protection enabled and leave any detection blocked. See
[the investigation record](docs/ANTIVIRUS-REPORT.md).

## Build and validate

Install Python 3.11+ and the .NET 8 SDK, then run:

```sh
python tools/build.py "/path/to/Gorilla Mod Manager 26 v2.0.4.zip"
```

Use `--dotnet /path/to/dotnet` if needed. The exact original user-supplied ZIP
is required; the source ZIP contains code only. Dependencies, NuGet lock files,
the SDK feature band and ILVerify tool are pinned. Initial restore needs NuGet
access. Original input hashes and normal TLS/package verification are enforced.

The build compiles the add-on and patcher, runs diagnostic/integration and
package regression tests, then patches `Doctor.Examine`'s two return paths and
the window constructor's return. It verifies that the other 3,954 method bodies,
all three original embedded resources and internal assembly identity remain
unchanged. Only the managed title/product attributes are rebranded; original
credits and the trusted update source are retained. Internal names and saved
data paths stay compatible with the original WPF resources and settings.
ILVerify checks both changed methods and every method in the add-on against
.NET Framework 4.8 reference assemblies.

The 74 diagnostic tests use actual model types from the supplied executable.
They cover existing fix delegates and suspects, crash evidence, local reports,
unavailable logs, write failures, supported CAK layouts, malformed entries,
read-only inspection, all duplicate owners, changed files, comparison budgets,
loader-message freshness and cautious crash conclusions. Seven package tests
enforce the exact file list, blocked-file hashes, dependencies and checksums.
The code-only source ZIP rejects binaries and nested input packages.

An optional scan can be required with `--clamscan /path/to/clamscan
--clamav-database /path/to/verified/definitions`. A failed scan stops packaging.
Without those arguments the package records that scanning was not run. A ClamAV
result would not constitute Microsoft Defender clearance.

Outputs are in `artifacts/`; original inputs and verification aliases stay in
ignored `build/` paths. The uploaded executable is never overwritten.

## Windows validation

The original desktop app and add-on require .NET Framework 4.8; the unchanged
`GorillaReader.dll` helper targets .NET 10. This Linux environment cannot run the
Windows interface, Defender or WWE 2K26. Actual startup, header layout, Event
Viewer access and a real game-crash workflow still require Windows validation.
Official updates may replace the added hooks and branding.

CAK format research: [Nenkai/Bakery](https://github.com/Nenkai/Bakery), revision
`f704d3724d713f36cff74a82e7466d5710b510de`. See
[the acknowledgment and MIT notice](docs/THIRD-PARTY-NOTICES.txt).
