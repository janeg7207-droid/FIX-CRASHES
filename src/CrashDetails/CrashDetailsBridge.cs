using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Gorilla.Manager.Engine;

namespace Gorilla.CrashDetails
{
    // Called only after the original Doctor has finished. Existing fixes and
    // suspect searches are kept; no game/mod files are changed by this add-on.
    public static class CrashDetailsBridge
    {
        public static Report Enhance(Report report, Core core, Func<DateTime> clock)
        {
            if (report == null || core == null || core.Places == null || !core.Places.GameFound) return report;
            try
            {
                DateTime now = clock == null ? DateTime.Now : clock();
                var crashes = (report.Crashes ?? new List<Crash>()).Where(c => c != null && c.When <= now)
                    .OrderByDescending(c => c.When).Select(ToEvidence).ToList();
                if (report.Findings == null) report.Findings = new List<Finding>();
                if (report.Looked == null) report.Looked = new List<string>();
                // Safe if a report is presented or refreshed more than once.
                report.Findings.RemoveAll(f => f != null && f.Id == "gorilla-crash-details");
                SoftenConclusions(report);

                string title;
                string details;
                bool recent = crashes.Count > 0 && crashes[0].When >= now.AddDays(-7);
                if (!CrashLog.Readable)
                {
                    title = "Crash cause unavailable: Windows records could not be read";
                    details = "Gorilla could not read Windows' Application event log. That is not evidence that the game did not crash.\n\n" +
                        "Open Windows Event Viewer > Windows Logs > Application and look near the time of the failure for WWE2K26_x64.exe (Application Error or Application Hang). " +
                        "Save the General/Details text for support. Other file checks and the existing mod search are still available.";
                    report.Headline = "I could not read the game's crash records.";
                    report.Line = "The crash cause is unknown. See the explanation below; your existing checks and restore controls are available.";
                    if (report.Mood == "good") report.Mood = "look";
                }
                else if (crashes.Count == 0)
                {
                    title = "No Windows crash record: the cause is unknown";
                    details = "Windows supplied no matching game crash or hang record for the period checked. Some exits, forced closes, power losses and failures do not produce one. " +
                        "This does not prove that the game is healthy or identify a bad mod.\n\n" +
                        "Keep Gorilla open, reproduce the same match/entrance/arena, then press Check Again on Fix My Game after Windows has written the record. " +
                        "If it is still absent, note the time and actions and check Event Viewer and CakeHook's own report. The existing mod search can help reproduce the failure.";
                    report.Line = "No matching Windows crash record was found. The exact cause is unknown; see the explanation and next checks below.";
                    if (report.Mood == "good")
                    {
                        report.Headline = "No crash cause has been identified.";
                        report.Mood = "look";
                    }
                }
                else
                {
                    CrashEvidence last = crashes[0];
                    Diagnosis diagnosis = Diagnosis.Explain(last, core.Places.GameDir, crashes);
                    title = "Latest recorded " + (last.Froze ? "freeze: " : "crash: ") + diagnosis.Summary;
                    details = diagnosis.DisplayText();
                    if (!recent)
                        details = "Historical record: this event is more than 7 days old. It may not explain a newer problem.\n\n" + details;
                    if (string.IsNullOrWhiteSpace(last.AppPath))
                        details += "\n\nThe event did not record the game's full path, so its installation could not be independently confirmed.";
                    var suspects = (report.Suspects ?? new List<Suspect>()).Where(s => s != null).Take(3).ToList();
                    if (suspects.Count > 0)
                    {
                        details += "\n\nOther leads from Gorilla (not confirmed causes):\n" + string.Join("\n", suspects.Select(s =>
                            "- " + s.Title + ": " + string.Join("; ", s.Why ?? new List<string>())));
                    }
                    if (recent)
                    {
                        report.Headline = "Latest " + (last.Froze ? "freeze: " : "crash: ") + diagnosis.Summary;
                        report.Line = "Recorded " + last.When.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) +
                            " on this PC. Read the crash details below for evidence, confidence and what to try next.";
                        report.Mood = "stop";
                    }
                }

                var finding = new Finding
                {
                    Id = "gorilla-crash-details",
                    Title = title,
                    Text = details,
                    Serious = recent || !CrashLog.Readable
                };
                report.Findings.Insert(0, finding);
                CakChecks.Append(report, core.Places);
                SaveReport(report, finding, core.Places.DataDir, now);
            }
            catch (Exception ex)
            {
                // A failed diagnostic must not prevent the original report or
                // disable any of its controls. Do not claim a cause on failure.
                if (report.Findings != null)
                    report.Findings.Add(new Finding
                    {
                        Id = "gorilla-crash-details-error",
                        Title = "The extra crash explanation could not be completed",
                        Text = "The original Gorilla checks are still available. Diagnostic error: " + ex.GetType().Name + ": " + ex.Message
                    });
            }
            return report;
        }

        private static CrashEvidence ToEvidence(Crash c) => new CrashEvidence
        {
            When = c.When, Module = c.Module, ModulePath = c.ModulePath,
            Code = c.Code, Offset = c.Offset, AppPath = c.AppPath, Froze = c.Froze
        };

        private static void SoftenConclusions(Report report)
        {
            foreach (Finding f in report.Findings.Where(f => f != null))
            {
                if (f.Id != null && f.Id.StartsWith("crash-in-", StringComparison.Ordinal))
                    f.Text = (f.Text ?? "").Replace(
                        "Windows recorded the crash in this plugin's own code, so the plugin is the cause.",
                        "Windows recorded the fault inside this plugin. It is a suspect, not a confirmed root cause: incompatible input or earlier memory corruption can also make it fail. Test the same scene with it off and restored to compare.");
                else if (f.Id == "fresh-crash")
                    f.Text = (f.Text ?? "").Replace("So your mods are not the cause.",
                        "Disabling mods did not prevent this failure. Base game files, saved/baked data, drivers and other software still need checking; one run cannot fully rule out previous mod changes.");
                else if (f.Id == "fresh-on" || f.Id == "try-fresh")
                    f.Text = (f.Text ?? "").Replace("if it crashes, it is the game itself; if it runs fine, it is one of your mods.",
                        "a repeat failure points beyond currently enabled mods; a successful comparable run makes a mod or plugin more likely, without proving it.")
                        .Replace("If it still crashes, it is the game itself. If it runs fine, it is one of your mods, and the search below finds which one.",
                        "If it still crashes, investigate game files, saved/baked data and drivers too. If a comparable run succeeds, the search below can help test mods and plugins.");
            }
            for (int i = 0; i < report.Looked.Count; i++)
                report.Looked[i] = (report.Looked[i] ?? "").Replace(": so the cause is in your mods or plugins", ": mods or plugins are leads to compare in repeated tests");
        }

        private static void SaveReport(Report report, Finding finding, string dataDir, DateTime now)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(dataDir)) throw new IOException("The Gorilla data folder is unavailable.");
                string folder = Path.Combine(dataDir, "Crash Reports");
                Directory.CreateDirectory(folder);
                var text = new StringBuilder();
                text.AppendLine("GORILLA MOD MANAGER 26 - CRASH DETAILS ADD-ON");
                text.AppendLine("Report generated (this PC): " + now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                text.AppendLine("This file is saved locally only. Review personal file paths before sharing it.");
                text.AppendLine();
                text.AppendLine(finding.Title);
                text.AppendLine(finding.Text);
                foreach (Finding f in report.Findings.Where(f => f != null && f != finding))
                {
                    text.AppendLine();
                    text.AppendLine("Existing Gorilla check: " + f.Title);
                    text.AppendLine(f.Text);
                }
                string destination = Path.Combine(folder, "Latest crash report.txt");
                // The size is bounded to one current report; refreshing does not
                // create an unbounded collection of files or upload anything.
                File.WriteAllText(destination, text.ToString(), new UTF8Encoding(false));
                finding.Folder = folder;
                finding.Text += "\n\nA local copy is saved as 'Latest crash report.txt'. Use Open folder on this card. It can contain personal file paths; review it before sharing.";
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException || ex is System.Security.SecurityException)
            {
                finding.Text += "\n\nThe explanation is shown here, but its text file could not be saved: " + ex.Message;
            }
        }
    }
}
