# Antivirus report: downloads withdrawn

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

The application ZIP and binary-containing source ZIP were removed from the
current branch. Git history still retains them; historical availability is not
an endorsement to download or execute them. No force-push or history rewrite
was performed. Source code and investigation evidence are retained.

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
  validation are still unavailable. No bypass, packing, obfuscation, filename
  change or binary mutation has been used to avoid the detection.

## Handling a downloaded copy

Keep the file blocked. Close the Inspector if it is running, use Windows
Security's removal action, and remove the downloaded ZIP and extracted copies.
Update Defender protection and run a Full scan. If remediation still fails,
use Microsoft Defender Offline scan from Windows Security's scan options.
Do not allow the detection or add an antivirus exclusion.

Microsoft's official file-review submission page is
https://www.microsoft.com/en-us/wdsi/filesubmission . No files have been submitted
to a third-party service by this investigation. A review result is still pending
external submission; this document does not claim one has been requested.
