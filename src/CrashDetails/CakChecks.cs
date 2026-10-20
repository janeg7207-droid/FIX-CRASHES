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
        private sealed class Owner { public string File; public string Path; }
        private sealed class Overlap
        {
            public string First, Second;
            public int Count;
            public readonly List<string> Examples = new List<string>();
        }

        public static void Append(Report report, Places places)
        {
            if (report.Findings == null) report.Findings = new List<Finding>();
            if (report.Looked == null) report.Looked = new List<string>();
            report.Findings.RemoveAll(f => f != null && f.Id != null && f.Id.StartsWith("gorilla-cak-", StringComparison.Ordinal));
            report.Looked.RemoveAll(s => s != null && s.StartsWith("CAK inspection:", StringComparison.Ordinal));
            try
            {
                string folder = places.ModsDir;
                var files = string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder) ? new List<string>() :
                    Directory.EnumerateFiles(folder).Where(f => f.EndsWith(".cak", StringComparison.OrdinalIgnoreCase))
                        .Take(257).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
                bool limitedFiles = files.Count > 256;
                if (limitedFiles) files.RemoveAt(256);
                int tablesRead = 0, errors = 0, limited = 0, detailed = 0;
                bool limitedHashes = false;
                var owners = new Dictionary<ulong, Owner>();
                var overlaps = new Dictionary<string, Overlap>(StringComparer.OrdinalIgnoreCase);
                var list = new StringBuilder();
                CakLoaderLog loader = CakLoaderLog.Read(places.GameDir);
                foreach (string file in files)
                {
                    CakInspection check = CakInspector.Inspect(file);
                    string name = Path.GetFileName(file);
                    if (check.TablesRead) tablesRead++;
                    else if (!check.HasErrors) limited++;
                    if (check.HasErrors) errors++;
                    list.AppendLine("- " + name + ": " + (check.HasErrors ? "problem found" : check.TablesRead ? check.FileCount + " archive entries; tables read" : "limited inspection") + "; FDIR " + (check.Version ?? "not identified"));
                    string loadEvidence = loader.ForFile(file);
                    if ((check.HasErrors || !check.TablesRead || check.External || check.FileCount == 0 || loadEvidence != null) && detailed < 20)
                    {
                        detailed++;
                        report.Findings.Add(new Finding
                        {
                            Id = "gorilla-cak-file-" + detailed,
                            Title = "CAK: " + name + (check.HasErrors ? " — problem found" : " — inspection notes"),
                            Text = check.Text() + (loadEvidence == null ? "" : "\n\n" + loadEvidence),
                            Serious = check.HasErrors,
                            Folder = folder
                        });
                    }
                    if (!check.TablesRead || check.HasErrors) continue;
                    foreach (CakAsset asset in check.Assets)
                    {
                        Owner owner;
                        if (owners.TryGetValue(asset.Hash, out owner) && !string.Equals(owner.File, name, StringComparison.OrdinalIgnoreCase))
                        {
                            string key = owner.File + "\0" + name;
                            Overlap pair;
                            if (!overlaps.TryGetValue(key, out pair))
                            {
                                if (overlaps.Count >= 64) { limitedHashes = true; continue; }
                                pair = new Overlap { First = owner.File, Second = name };
                                overlaps.Add(key, pair);
                            }
                            pair.Count++;
                            if (pair.Examples.Count < 3) pair.Examples.Add(asset.Path + " [" + asset.Hash.ToString("X16") + "]");
                        }
                        else if (!owners.ContainsKey(asset.Hash))
                        {
                            if (owners.Count >= 200000) { limitedHashes = true; continue; }
                            owners.Add(asset.Hash, new Owner { File = name, Path = asset.Path });
                        }
                    }
                }
                foreach (Overlap pair in overlaps.Values.Take(12))
                    report.Findings.Add(new Finding
                    {
                        Id = "gorilla-cak-overlap-" + pair.First + "-" + pair.Second,
                        Title = "CAKs share asset IDs: " + pair.First + " and " + pair.Second,
                        Text = "These enabled archives share " + pair.Count + " asset ID(s). Samples:\n" + string.Join("\n", pair.Examples) +
                            "\n\nThis can be an intentional replacement, not a broken file or a proven crash cause. The loader may choose one version of a shared asset. If content is missing or the same scene fails, compare these CAKs separately through Gorilla's existing mod-off and restore controls. The scan does not determine the loader's chosen winner.",
                        Folder = folder
                    });
                string text = files.Count == 0
                    ? "No enabled .cak files were found directly in the configured mods folder. Use Add a Mod to install one, or open Gorilla CAK Inspector.exe to choose a file before installing it. Disabled files and subfolders are not scanned here."
                    : "Read " + files.Count + " enabled CAK file(s): " + tablesRead + " supported archive table(s) read, " + errors + " file(s) with structural/read problems, " + limited + " with limited inspection.\n\n" + list;
                text += "\n\n" + loader.Status;
                text += "\n\nThis scan reads CAK bytes and supported file/directory tables; it does not just check filenames or sizes. Shared asset IDs are leads, not proof of a conflict. Encrypted tables, unsupported versions and compressed asset contents are not validated. A clean structural scan does not prove that a mod works in WWE 2K26.";
                if (limitedFiles || limitedHashes || overlaps.Count > 12)
                    text += "\n\nScan limits reached: at most 256 archives, 200,000 distinct asset IDs and 12 overlap cards are shown. Additional files/overlaps are unverified. Use the standalone CAK Inspector for a particular file.";
                text += "\n\nOnly enabled, top-level .cak files in the configured mods folder are scanned. No CAK is changed, extracted, deleted or switched off by this check.";
                if (detailed == 20) text += "\n\nFile-specific cards are limited to 20. Check the summary's file list and use the standalone CAK Inspector for any file without its own card.";
                report.Findings.Add(new Finding { Id = "gorilla-cak-summary", Title = "CAK file inspection", Text = text, Folder = Directory.Exists(folder) ? folder : null });
                report.Looked.Add("CAK inspection: " + files.Count + " file(s), " + tablesRead + " supported table(s), " + errors + " problem file(s)");
                if (errors > 0)
                {
                    report.Mood = "stop";
                    report.Line = (report.Line ?? "") + " CAK inspection found " + errors + " structural/read problem(s); see the file-specific cards. These do not by themselves prove a crash cause.";
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException || ex is System.Security.SecurityException)
            {
                report.Findings.Add(new Finding { Id = "gorilla-cak-unavailable", Title = "CAK folder inspection could not finish", Text = "No file validity is claimed. " + ex.Message + ". Use Gorilla CAK Inspector.exe to select a readable file directly." });
            }
        }
    }
}
