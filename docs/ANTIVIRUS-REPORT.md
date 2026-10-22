# Antivirus report: flagged download withdrawn, current builds unverified

Microsoft Defender reported `Trojan:Win32/Ulthar.A!ml` for
`Gorilla CAK Inspector.exe` inside the application ZIP on October 20, 2026.
The reported status was **Quarantine failed / Remediation incomplete**.
The cause of the detection has not been established. It must not be dismissed
as a false positive or treated as an unsigned-application warning.

## Identifying the reported build

- Application ZIP: `Gorilla-Mod-Manager-26-v2.0.4-Crash-and-CAK-Checks.zip`
- Published ZIP SHA-256: `e53fcff8b0e2ecea02eb2488107bffa27e1f0ca99fda5f213d3e4bea73194dff`
- Flagged file: `Gorilla CAK Inspector.exe`
- Packaged Inspector SHA-256: `c1ab7774bb5cce98c76e569f9e8bf437e1a45ece3cc6699429149a862f23c920`
- Original upload commit: `46891daf7a0399ee2e4797c9ecd6cb44ec167ac4`

The original application ZIP and binary-containing source ZIP were removed from
the current branch. Git history still retains them; historical availability is not
an endorsement to download or execute them. No force-push or history rewrite
was performed. Source code and investigation evidence are retained.

## r2 remediation and remaining verification

At the user's request, r2 removes the entire standalone Inspector executable,
its configuration and its build project. Its existing CAK parsing/checking logic
remains part of the manager's Fix My Game report. No executable was renamed,
packed or obfuscated as a replacement for the reported standalone program.
The manager's original features remain in place.

The revised package has an exact file manifest and rejects the previously
reported file's SHA-256, extra executables, unexpected files and checksum
mismatches. Seven package regression tests cover these checks. The source ZIP
contains code only and no nested original executable package.

This is a component-removal revision, **not confirmed malware remediation or a
Microsoft false-positive verdict**. The revised application is published with
antivirus clearance explicitly unverified. The older flagged build remains
withdrawn and must remain blocked.

A local ClamAV 1.4.3 engine was obtained using Debian's signed package indices
and verified package downloads. The official virus-definition update failed:
the network proxy returned `CONNECT tunnel failed, response 403` for
`database.clamav.net`. No definitions were substituted and no partial or zero-
signature scan was reported as a passing malware check. Microsoft Defender is
unavailable in this Linux environment. See the actual package's
`ANTIVIRUS-STATUS.txt`; neither scanner has cleared the r2 package here.

## r3 branding and file comparisons

The user subsequently requested the name **Mykelovves Mod Manager** and
comparisons showing matching files and potential crash leads. The main launch
file is now `Mykelovves Mod Manager.exe`; its title/header and the diagnostic
DLL use that branding. Internal original assembly identity, author credits,
resources, data paths and trusted update source remain. This requested product
rename does not establish antivirus safety and is not a replacement or renamed
copy of the removed standalone Inspector. The exact manifest and blocked-hash
checks remain in place. Antivirus clearance for r3 is also **unverified**.

## r4 duplicate-files tab

r4 adds a page inside the existing manager for CAK duplicate comparisons,
enabled/disabled labels, search, character/roster filtering, cancellable larger-file
hashing and local text export. It includes Parsec/character-selection investigation
guidance without asserting a crash cause. No standalone executable is added.
The original blocked Inspector hash and exact package manifest are still enforced.
No new antivirus scan or Microsoft review has completed; r4 clearance remains
**unverified**. The functional test results are not malware-scan results.

## Established facts and limits

- The archive's integrity check passes and its Inspector executable matches the
  local compiled output. This rules out a mismatch with that output, not malware.
- The new add-on DLL and modified manager also match their local build outputs.
  The original reader DLL and runtime settings are unchanged from the supplied ZIP.
- The Windows executables have no Authenticode certificate table. Lack of a
  publisher signature does not explain or invalidate the specific Trojan alert.
- The new Inspector source implements a Windows Forms file picker/drag-and-drop
  interface, read-only CAK inspection, and user-selected text-report saving. A
  review of that source found no command-shell execution or network client calls.
  This limited source review is not an antivirus verdict or runtime security audit.
- The previous 54 checks exercised functionality, metadata, and IL validity. They
  did not execute Microsoft Defender, perform a malware scan, or test Windows/game
  operation. No malware-free claim can be made from those results.
- A confirmed Microsoft Defender classification/review and Windows security
  validation are still unavailable. The r2 changes remove the standalone feature;
  no antivirus bypass, packing or obfuscation has been introduced.

## Handling a downloaded copy

Keep the file blocked. Close the Inspector if it is running, use Windows
Security's removal action, and remove the downloaded ZIP and extracted copies.
Update Defender protection and run a Full scan. If remediation still fails,
use Microsoft Defender Offline scan from Windows Security's scan options.
Do not allow the detection or add an antivirus exclusion.

Microsoft's official file-review submission page is
https://www.microsoft.com/en-us/wdsi/filesubmission . No files have been submitted
to a third-party service by this investigation. No vendor review has been
requested or obtained.
