using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Gorilla.Manager.Engine;

namespace Gorilla.CrashDetails
{
    public static class CakChecks
    {
        public static void Append(Report report, Places places, DateTime? examinedAt = null)
        {
            if (report.Findings == null) report.Findings = new List<Finding>();
            if (report.Looked == null) report.Looked = new List<string>();
            report.Findings.RemoveAll(f => f != null && f.Id != null && f.Id.StartsWith("gorilla-cak-", StringComparison.Ordinal));
            report.Looked.RemoveAll(s => s != null && s.StartsWith("CAK inspection:", StringComparison.Ordinal));
            try
            {
                DateTime now = examinedAt ?? DateTime.Now;
                string folder = places.ModsDir;
                var files = string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder) ? new List<string>() :
                    Directory.EnumerateFiles(folder).Where(f => f.EndsWith(".cak", StringComparison.OrdinalIgnoreCase))
                        .Take(257).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
                bool limitedFiles = files.Count > 256, limitedIndex = false;
                if (limitedFiles) files.RemoveAt(256);
                int tablesRead = 0, errors = 0, limited = 0, detailed = 0, retained = 0;
                var inspected = new List<CakInspection>();
                var leads = new List<FileCrashLead>();
                var fileProblems = new StringBuilder();
                var list = new StringBuilder();
                CakLoaderLog loader = CakLoaderLog.Read(places.GameDir);
                foreach (string file in files)
                {
                    CakInspection check = CakInspector.Inspect(file);
                    string name = Path.GetFileName(file);
                    if (check.TablesRead) tablesRead++;
                    else if (!check.HasErrors) limited++;
                    if (check.HasErrors)
                    {
                        errors++;
                        fileProblems.AppendLine(check.Text() + "\n\n");
                        foreach (CakIssue issue in check.Issues.Where(i => i.Error).Take(2))
                            leads.Add(new FileCrashLead
                            {
                                Priority = issue.AssetPath == null ? 90 : 100,
                                Title = name + (issue.AssetPath == null ? ": read/structure problem" : " -> " + issue.AssetPath),
                                Evidence = "Confirmed file-read/structure problem: " + issue.Message + " Its connection to the game crash remains unconfirmed.",
                                Next = "Close copying/baking tools and retry. If the structural problem persists, restore or obtain a fresh compatible copy; compare the same scene with this archive disabled and restored using the existing controls."
                            });
                    }
                    list.AppendLine("- " + name + ": " + (check.HasErrors ? "problem found" : check.TablesRead ? check.FileCount + " archive entries; tables read" : "limited inspection") + "; FDIR " + (check.Version ?? "not identified"));
                    string loadEvidence = loader.ForFile(file);
                    string loaderError = loader.RecentErrorForFile(file, now);
                    if (loaderError != null)
                        leads.Add(new FileCrashLead
                        {
                            Priority = 80,
                            Title = "Recent CakeHook error text mentions " + name,
                            Evidence = loaderError + "\nThe last marked session's report is newer than this CAK copy. A quoted error is evidence to investigate, not proof that it caused the game crash.",
                            Next = "Check the exact loader message and this CAK's requirements. Reproduce the same scene after a compatible rebake or controlled mod-off/restore test."
                        });
                    if ((check.HasErrors || !check.TablesRead || check.External || check.FileCount == 0 || loadEvidence != null) && detailed < 20)
                    {
                        detailed++;
                        report.Findings.Add(new Finding
                        {
                            Id = "gorilla-cak-file-" + detailed,
                            Title = "CAK: " + name + (check.HasErrors ? " — problem found" : " — inspection notes"),
                            Text = check.Text() + (loadEvidence == null ? "" : "\n\n" + loadEvidence),
                            Serious = check.HasErrors, Folder = folder
                        });
                    }
                    int room = Math.Max(0, CakComparison.MaxIndexedEntries - retained);
                    if (check.Assets.Count > room)
                    {
                        check.Assets.RemoveRange(room, check.Assets.Count - room);
                        limitedIndex = true;
                    }
                    retained += check.Assets.Count;
                    inspected.Add(check);
                }

                CakComparisonResult comparison = CakComparison.Compare(inspected);
                comparison.Limited |= limitedFiles || limitedIndex;
                int number = 0;
                foreach (var duplicate in comparison.IdenticalArchives)
                    report.Findings.Add(new Finding
                    {
                        Id = "gorilla-cak-duplicate-" + ++number,
                        Title = "Identical whole CAK files: " + string.Join(", ", duplicate.Names),
                        Text = "These full archives have the same SHA-256: " + duplicate.Sha256 +
                            "\nFiles: " + string.Join(", ", duplicate.Names) +
                            "\nThis establishes identical archived bytes for the compared snapshots. Duplicate copies alone do not establish a crash cause. Use the existing duplicate/mod controls if you want to keep one enabled; nothing is changed automatically.",
                        Folder = folder
                    });
                var groups = comparison.SharedFiles.OrderByDescending(g => g.DifferentStoredBytes).ThenBy(g => g.AllCompared).ThenBy(g => g.Copies[0].Asset.Path, StringComparer.OrdinalIgnoreCase).ToList();
                number = 0;
                foreach (CakSharedFile shared in groups.Take(12))
                    report.Findings.Add(new Finding
                    {
                        Id = "gorilla-cak-overlap-" + ++number,
                        Title = shared.Status + ": " + shared.Copies[0].Asset.Path,
                        Text = shared.Text(), Folder = folder
                    });
                Crash last = CrashLog.Readable ? (report.Crashes ?? new List<Crash>()).Where(c => c != null && c.When <= now && c.When >= now.AddDays(-7)).OrderByDescending(c => c.When).FirstOrDefault() : null;
                foreach (CakSharedFile shared in groups.Where(g => g.DifferentStoredBytes).Take(8))
                {
                    bool recent = last != null && shared.Copies.Any(c => c.Archive.WriteUtcTicks <= last.When.ToUniversalTime().Ticks && c.Archive.WriteUtcTicks >= last.When.AddDays(-1).ToUniversalTime().Ticks);
                    leads.Add(new FileCrashLead
                    {
                        Priority = recent ? 65 : 50,
                        Title = "Different stored versions: " + shared.Copies[0].Asset.Path,
                        Evidence = "CAKs: " + string.Join(", ", shared.Copies.Select(c => c.Name)) + ". Compared payload hashes differ." +
                            (recent ? " At least one archive changed within 24 hours before the latest recorded crash; timing is a clue, not proof." : "") +
                            " Intentional overrides or different compression can explain this. No specific file is confirmed to have caused the crash.",
                        Next = "Compare the named archives separately in the same scene, then restore each one. Use the same-files report to see the exact internal paths and byte groups."
                    });
                }
                CrashLeads.Append(report, leads);
                string text = files.Count == 0
                    ? "No enabled .cak files were found directly in the configured mods folder. Use the existing Add a Mod page to add a CAK, then return here and press Check Again. Disabled files and subfolders are not scanned here."
                    : "Read " + files.Count + " enabled CAK file(s): " + tablesRead + " supported archive table(s) read, " + errors + " file(s) with structural/read problems, " + limited + " with limited inspection.\n\n" + list;
                text += "\n\nWhole-archive duplicate groups: " + comparison.IdenticalArchives.Count + ". Shared internal-file groups: " + comparison.SharedFiles.Count +
                    "; different stored versions: " + comparison.SharedFiles.Count(g => g.DifferentStoredBytes) + ". Comparison reads: " + comparison.BytesRead + " bytes.";
                text += "\n\n" + loader.Status;
                text += "\n\nThis scan reads CAK bytes, supported tables and bounded payload ranges. It distinguishes shared paths from matching stored bytes. Compressed/encrypted contents are not decoded. A clean structural check or matching file bytes do not establish game compatibility or the exact crash cause.";
                if (comparison.Limited || limited > 0 || groups.Count > 12)
                    text += "\n\nPartial results/limits: up to 256 archives, 200,000 indexed entries, 1,000 shared file groups and 12 visible shared-file cards. Byte comparisons are limited to 256 MiB and 15 seconds overall, 64 MiB per whole CAK and 32 MiB per asset. Omitted or unreadable copies are unverified, not different or healthy.";
                if (detailed == 20) text += "\n\nFile-specific cards are limited to 20; the summary lists all inspected archive names.";
                text += "\n\nOnly enabled, top-level CAKs are scanned. No game/mod file is extracted, changed, deleted or switched off.";
                string reportFolder = SaveComparison(places.DataDir, comparison.FullText() + "\n\nFILE PROBLEMS\n" + fileProblems + "\nCRASH INVESTIGATION\n" + report.Findings.Last(f => f.Id == "gorilla-cak-crash-leads").Text, out string saveError);
                text += reportFolder == null ? "\n\nThe same-files report could not be saved: " + saveError :
                    "\n\nFull comparison list: 'Same files report.txt'. Use Open folder on this card. It includes every reported archive name, internal path and available SHA-256; review personal paths before sharing.";
                report.Findings.Add(new Finding { Id = "gorilla-cak-summary", Title = "Same files and CAK inspection", Text = text, Folder = reportFolder ?? (Directory.Exists(folder) ? folder : null) });
                report.Looked.Add("CAK inspection: " + files.Count + " file(s), " + tablesRead + " supported table(s), " + errors + " problem file(s)");
                if (errors > 0)
                {
                    report.Mood = "stop";
                    report.Line = (report.Line ?? "") + " CAK inspection found " + errors + " structural/read problem(s); see the named files and crash investigation cards. These do not by themselves prove a crash cause.";
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException || ex is System.Security.SecurityException)
            {
                report.Findings.Add(new Finding { Id = "gorilla-cak-unavailable", Title = "CAK folder inspection could not finish", Text = "No file validity is claimed. " + ex.Message + ". Check Folders, close copying/baking tools, then press Check Again." });
            }
        }

        private static string SaveComparison(string dataDir, string text, out string error)
        {
            error = null;
            try
            {
                if (string.IsNullOrWhiteSpace(dataDir)) { error = "The manager's data folder is unavailable."; return null; }
                string folder = Path.Combine(dataDir, "Crash Reports");
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "Same files report.txt"), text, new UTF8Encoding(false));
                return folder;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException || ex is System.Security.SecurityException)
            { error = ex.Message; return null; }
        }
    }
}
