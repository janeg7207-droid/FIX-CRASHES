using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Gorilla.CrashDetails
{
    public sealed class CrashEvidence
    {
        public DateTime When;
        public string Module;
        public string ModulePath;
        public string Code;
        public string Offset;
        public string AppPath;
        public bool Froze;
    }

    public sealed class Diagnosis
    {
        public string Summary;
        public string Confidence;
        public string Explanation;
        public string NextStep;
        public string Evidence;

        public string DisplayText()
        {
            return "What happened: " + Explanation + "\n\nConfidence: " + Confidence +
                "\n\nWindows recorded:\n" + Evidence + "\n\nWhat to try next: " + NextStep;
        }

        public static Diagnosis Explain(CrashEvidence crash, string gameDir, IEnumerable<CrashEvidence> history)
        {
            if (crash == null) throw new ArgumentNullException(nameof(crash));
            string module = FileName(crash.ModulePath);
            if (string.IsNullOrWhiteSpace(module)) module = FileName(crash.Module);
            string lower = (module ?? "").ToLowerInvariant();
            string code = NormalizeCode(crash.Code);
            var d = new Diagnosis
            {
                Summary = "The exact cause is not identified",
                Confidence = "Unknown root cause. A Windows crash record describes the failure, not necessarily the mod that caused it.",
                Explanation = ExplainCode(code),
                NextStep = "Use the existing 'Try this first' suggestions or the mod search on Fix My Game. Repeat the same match, arena, entrance and actions with a suspect off, then back on, to see whether the failure follows it. Keep the backup and restore controls available."
            };

            if (crash.Froze)
            {
                d.Summary = "The game stopped responding";
                d.Explanation = "Windows recorded an application hang. This is a freeze, not an exception report; it does not identify which mod, plugin or game subsystem stopped responding.";
                d.NextStep = "Try the same scene again. Note whether it freezes during loading, an entrance or a match. Use the existing mod search to compare runs; a crash dump or hang dump may be needed if no mod consistently reproduces it.";
            }
            else if (IsInsidePlugins(crash.ModulePath, gameDir))
            {
                d.Summary = "Plugin involved: " + module;
                d.Confidence = "Plugin involvement recorded; the underlying cause is unconfirmed.";
                d.Explanation += " Windows recorded the failure inside " + module + ". That makes this plugin a useful suspect, but another mod, incompatible data or an earlier memory error can make a plugin fail.";
                bool cakeHook = lower.Contains("cakehook");
                d.NextStep = cakeHook
                    ? "Check that CakeHook matches your exact game version. Keep CakeHook enabled for the existing mod search; test recently changed mods and other plugins through the manager's backup and restore controls. A CakeHook fault alone does not identify a bad mod."
                    : "Check that " + module + " supports your exact game version. Use the existing plugin-off control to test without it, then repeat with it restored. If the same crash follows the plugin repeatedly, that strengthens the evidence; do not delete it.";
            }
            else if (lower.StartsWith("parsec", StringComparison.Ordinal) && lower.EndsWith(".dll", StringComparison.Ordinal))
            {
                d.Summary = "Fault recorded in a Parsec-named component";
                d.Confidence = "The component name is recorded; Parsec or a particular mod is not established as the root cause.";
                d.Explanation += " The recorded DLL name starts with Parsec. This is a component clue, not proof that remote input or Parsec itself caused the failure.";
                d.NextStep = "Repeat the same characters, attires and selection order locally with Parsec fully closed, then with Parsec connected while only the host selects, then with one guest controller at a time. " +
                    "Use Duplicate files > Character/roster paths and the Parsec character-selection checks. If it crashes locally too, compare the named character mods through the existing backup and mod-search controls.";
            }
            else if (IsGraphicsModule(lower) || IsGraphicsCode(code))
            {
                d.Summary = "Graphics-related failure recorded";
                d.Confidence = "Graphics involvement is a clue; a driver, graphics setting, overlay or modded asset may be responsible.";
                d.Explanation += " The recorded component or error points to the graphics path. This does not prove that the graphics driver itself is defective.";
                d.NextStep = "Repeat the failing scene with a known-compatible graphics driver and overlays disabled. Reduce graphics settings or VRAM use, then compare with the same modded textures/arena disabled through the manager. Restore each change separately to identify which one matters.";
            }
            else if (code == "0xC0000017" || code == "0x8007000E")
            {
                d.Summary = "A memory allocation failed";
                d.Confidence = "The allocation failure is recorded; the reason for memory pressure is unconfirmed.";
                d.NextStep = "Check RAM/commit use and free disk space for the Windows page file. Close heavy applications and test with fewer high-resolution assets. This error does not by itself prove a defective RAM stick or a particular mod.";
            }
            else if (code == "0xC0000135" || code == "0xC000007B" || code == "0xC0000142")
            {
                d.Summary = "A component could not load or initialize";
                d.Confidence = "The loader failure is recorded; the missing or incompatible dependency may need a separate loader log.";
                d.NextStep = "Verify the game files in Steam and check the failing plugin's official runtime and 64-bit requirements. Install dependencies from their official publishers. Do not download replacement system DLLs from DLL download sites.";
            }
            else if (lower == "kernelbase.dll" || lower == "ntdll.dll" || lower == "kernel32.dll")
            {
                d.Summary = "Windows recorded the failure in a shared system component";
                d.Explanation += " " + module + " is used by many applications. Its name in the record does not mean Windows caused the crash or identify the bad mod.";
            }

            var evidence = new StringBuilder();
            evidence.AppendLine("Time (this PC): " + crash.When.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            evidence.AppendLine("Event: " + (crash.Froze ? "application hang" : "application crash"));
            evidence.AppendLine("Game: " + Present(crash.AppPath));
            evidence.AppendLine("Faulting component: " + Present(module));
            evidence.AppendLine("Component path: " + Present(crash.ModulePath));
            evidence.AppendLine("Exception code: " + (crash.Froze ? "not supplied for this hang" : Present(code)));
            evidence.Append("Fault offset: " + (crash.Froze ? "not supplied for this hang" : Present(crash.Offset)));
            if (!crash.Froze && !string.IsNullOrWhiteSpace(module) &&
                !string.IsNullOrWhiteSpace(code) && !string.IsNullOrWhiteSpace(crash.Offset))
            {
                int count = (history ?? new CrashEvidence[0]).Count(c => c != null && !c.Froze &&
                    c.When >= crash.When.AddDays(-7) && c.When <= crash.When &&
                    string.Equals(FileName(c.ModulePath) ?? FileName(c.Module), module, StringComparison.OrdinalIgnoreCase) &&
                    NormalizeCode(c.Code) == code &&
                    string.Equals(c.Offset, crash.Offset, StringComparison.OrdinalIgnoreCase));
                if (count > 1)
                    evidence.Append("\nMatching component/code/offset in the preceding 7 days: " + count +
                        ". A matching signature is evidence of a similar failure, not proof of the same root cause.");
            }
            d.Evidence = evidence.ToString();
            return d;
        }

        public static string NormalizeCode(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            string text = value.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text.Substring(2);
            uint number;
            return uint.TryParse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out number)
                ? "0x" + number.ToString("X8", CultureInfo.InvariantCulture)
                : value.Trim();
        }

        public static bool IsInsidePlugins(string path, string gameDir)
        {
            string full = CanonicalWindowsPath(path);
            string root = CanonicalWindowsPath(gameDir);
            return full != null && root != null && full.StartsWith(root.TrimEnd('\\') + "\\Plugins\\", StringComparison.OrdinalIgnoreCase);
        }

        private static string CanonicalWindowsPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            string text = path.Trim().Replace('/', '\\');
            if (!((text.Length >= 3 && char.IsLetter(text[0]) && text[1] == ':' && text[2] == '\\') || text.StartsWith("\\\\", StringComparison.Ordinal))) return null;
            var parts = new List<string>();
            foreach (string part in text.Split('\\'))
            {
                if (part == ".") continue;
                if (part == "..")
                {
                    if (parts.Count == 0) return null;
                    parts.RemoveAt(parts.Count - 1);
                }
                else parts.Add(part);
            }
            return string.Join("\\", parts);
        }

        private static string FileName(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            return path.Replace('/', '\\').Split('\\').Last();
        }

        private static string Present(string value) => string.IsNullOrWhiteSpace(value) ? "not recorded" : value;

        private static bool IsGraphicsModule(string module)
        {
            return new[] { "dxgi.dll", "d3d11.dll", "d3d12.dll", "d3d12core.dll", "nvwgf2umx.dll", "nvldumdx.dll", "atidxx64.dll", "atio6axx.dll", "amdxx64.dll", "igd10iumd64.dll", "igd12umd64.dll" }.Contains(module);
        }

        private static bool IsGraphicsCode(string code) => code == "0x887A0005" || code == "0x887A0006" || code == "0x887A0007";

        private static string ExplainCode(string code)
        {
            switch (code)
            {
                case "0xC0000005": return "The game tried to read, write or execute an invalid memory address (access violation). Incompatible plugins, bad data or an earlier memory error are possibilities; this code alone cannot name a mod.";
                case "0xC0000409": return "The process was terminated by a fail-fast or security check. Despite the historical code name, this does not by itself prove a stack buffer overrun; a dump is needed for the specific reason.";
                case "0xC0000374": return "Windows detected heap memory corruption. The component that detects the damage may differ from the one that originally caused it.";
                case "0xE06D7363": return "An unhandled Microsoft C++ exception terminated the process. Its message and call stack are needed to identify the underlying failure.";
                case "0xE0434352": return "An unhandled .NET exception terminated the process. The .NET exception message and call stack are needed to identify the underlying failure.";
                case "0xC00000FD": return "A thread exhausted its call stack (stack overflow), often because of excessive or repeated nested calls. The record alone does not identify which mod triggered them.";
                case "0xC000001D": return "The CPU encountered an illegal instruction. Unsupported instructions, incompatible code or corrupted control flow are possibilities.";
                case "0xC0000094": return "Code attempted integer division by zero. The faulting component is a useful lead, but the triggering input is not identified by this record.";
                case "0xC0000017": case "0x8007000E": return "A memory allocation failed. The record does not distinguish system memory pressure, address-space limits or a particular allocation problem.";
                case "0xC0000006": return "A memory-mapped page could not be read (in-page error). Check disk/storage errors and file integrity; the underlying I/O status requires a fuller crash report.";
                case "0xC0000135": return "A required DLL dependency could not be found.";
                case "0xC000007B": return "Windows could not load a valid executable image, for example because of an incompatible architecture or damaged component.";
                case "0xC0000142": return "A DLL failed to initialize. The record alone may not identify the dependency or configuration responsible.";
                case "0x80000003": return "A breakpoint exception ended the process. This may be an intentional abort or debugging assertion; a call stack is needed.";
                case "0x887A0005": return "The graphics device was removed or became unavailable (DXGI device removed).";
                case "0x887A0006": return "The graphics device stopped responding to commands (DXGI device hung).";
                case "0x887A0007": return "The graphics device was reset (DXGI device reset).";
                default: return string.IsNullOrWhiteSpace(code)
                    ? "Windows did not provide an exception code. The available record cannot establish the exact cause."
                    : "Windows recorded exception code " + code + ". This report has no specific interpretation for that code; a crash dump or the mod/plugin author's logs may be needed.";
            }
        }
    }
}
