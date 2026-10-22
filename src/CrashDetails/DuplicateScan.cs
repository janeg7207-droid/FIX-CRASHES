using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Gorilla.Manager.Engine;

namespace Gorilla.CrashDetails
{
    public sealed class DuplicateRow
    {
        public string Kind { get; set; }
        public string File { get; set; }
        public string Archives { get; set; }
        public string Activity { get; set; }
        public string Details { get; set; }
        public bool CharacterRelated { get; set; }
    }

    public sealed class DuplicateScanResult
    {
        public readonly List<DuplicateRow> Rows = new List<DuplicateRow>();
        public readonly List<string> Notes = new List<string>();
        public int Scanned;
        public bool Limited;
        public bool IncludeDisabled;
        public bool LargeFiles;
        public string ModsFolder;
        public string DisabledFolder;
        public DateTime Created;
        public string Summary => Scanned + " CAKs inspected; " + Rows.Count(r => r.Kind == "Identical CAKs") + " whole-archive duplicate group(s); " +
            Rows.Count(r => r.Kind == "Identical stored files") + " identical stored-file group(s); " + Rows.Count(r => r.Kind == "Different versions") + " different-version group(s)." +
            (Limited ? " Partial inspection: read the scan notes." : "");

        public IEnumerable<DuplicateRow> Filter(string kind, string search, bool charactersOnly)
        {
            return Rows.Where(r => (string.IsNullOrEmpty(kind) || kind == "All results" || r.Kind == kind) &&
                (!charactersOnly || r.CharacterRelated) && (string.IsNullOrWhiteSpace(search) ||
                (r.File + "\n" + r.Archives + "\n" + r.Activity + "\n" + r.Details).IndexOf(search.Trim(), StringComparison.OrdinalIgnoreCase) >= 0));
        }

        public string FullText()
        {
            var text = new StringBuilder("MYKELOVVES MOD MANAGER — DUPLICATE FILES\n");
            text.AppendLine("Scan time (this PC): " + Created.ToString("yyyy-MM-dd HH:mm:ss"));
            text.AppendLine("Enabled folder: " + ModsFolder);
            text.AppendLine("Include disabled CAKs: " + IncludeDisabled + "; disabled folder: " + DisabledFolder + "; compare large CAKs: " + LargeFiles);
            text.AppendLine(Summary);
            foreach (string note in Notes) text.AppendLine("NOTE: " + note);
            foreach (DuplicateRow row in Rows)
                text.AppendLine("\n" + row.Kind + ": " + row.File + "\n" + row.Activity + "\n" + row.Details);
            return text.ToString();
        }
    }

    public static class DuplicateScan
    {
        public const int MaxFiles = 256;
        private const int MaxFolderEntries = 4096;

        public static DuplicateScanResult Run(Places places, bool includeDisabled = false, bool largeFiles = false,
            CancellationToken cancellationToken = default, IProgress<string> progress = null)
        {
            if (places == null) throw new ArgumentNullException(nameof(places));
            cancellationToken.ThrowIfCancellationRequested();
            var result = new DuplicateScanResult { Created = DateTime.Now, ModsFolder = places.ModsDir,
                DisabledFolder = places.OffDir, IncludeDisabled = includeDisabled, LargeFiles = largeFiles };
            var active = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            Collect(places.ModsDir, true);
            if (includeDisabled) Collect(places.OffDir, false);
            var inspected = new List<CakInspection>();
            int retained = 0;
            foreach (var file in active)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report("Reading CAK " + (inspected.Count + 1) + " of " + active.Count + ": " + Path.GetFileName(file.Key));
                cancellationToken.ThrowIfCancellationRequested();
                var check = CakInspector.Inspect(file.Key);
                int room = Math.Max(0, CakComparison.MaxIndexedEntries - retained);
                if (check.Assets.Count > room) { check.Assets.RemoveRange(room, check.Assets.Count - room); result.Limited = true; }
                retained += check.Assets.Count;
                inspected.Add(check);
                if (check.HasErrors || !check.TablesRead)
                {
                    result.Limited = true;
                    result.Rows.Add(new DuplicateRow { Kind = check.HasErrors ? "File problem" : "Unverified", File = Path.GetFileName(file.Key),
                        Archives = Path.GetFileName(file.Key), Activity = State(file.Key), Details = check.Text() + "\nLocation: " + Location(file.Key),
                        CharacterRelated = check.Issues.Any(i => CharacterPath(i.AssetPath)) });
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report("Comparing stored bytes. You can cancel while this runs.");
            var comparison = CakComparison.Compare(inspected,
                byteBudget: largeFiles ? 8L * 1024 * 1024 * 1024 : CakComparison.DefaultByteBudget,
                timeBudgetMilliseconds: largeFiles ? 60000 : 15000,
                maxArchiveBytes: largeFiles ? 4L * 1024 * 1024 * 1024 : 64L * 1024 * 1024,
                maxPayloadBytes: largeFiles ? 256L * 1024 * 1024 : 32L * 1024 * 1024, cancellationToken: cancellationToken);
            result.Scanned = inspected.Count;
            result.Limited |= comparison.Limited;
            foreach (CakArchiveCopies group in comparison.IdenticalArchives)
                result.Rows.Add(new DuplicateRow { Kind = "Identical CAKs", File = string.Join(", ", group.Names), Archives = string.Join(", ", group.Names),
                    Activity = Activity(group.Paths), Details = "Whole-file SHA-256: " + group.Sha256 + "\n" + string.Join("\n", group.Paths.Select(Location)) +
                        "\nThese archive snapshots have identical bytes. A disabled copy does not form an active conflict. Duplicates alone do not prove a crash cause." });
            foreach (CakSharedFile group in comparison.SharedFiles)
                result.Rows.Add(new DuplicateRow { Kind = !group.SamePath ? "Unverified" : group.DifferentStoredBytes ? "Different versions" :
                    group.AllCompared ? "Identical stored files" : "Unverified", File = group.Copies[0].Asset.Path,
                    Archives = string.Join(", ", group.Copies.Select(c => c.Name)), Activity = Activity(group.Copies.Select(c => c.Archive.FilePath)),
                    Details = group.Text() + "\n\nLocations:\n" + string.Join("\n", group.Copies.Select(c => Location(c.Archive.FilePath))),
                    CharacterRelated = group.Copies.Any(c => CharacterPath(c.Asset.Path)) });
            result.Notes.Add("Results are snapshots. Scan again after changing, copying or baking mods. No file was extracted, changed, deleted or switched off.");
            result.Notes.Add("Disabled copies are labelled and do not establish a current conflict. Stored-byte differences can be intentional overrides or different compression; they do not prove the game crash cause.");
            result.Notes.Add(largeFiles ? "Large-file comparison: up to 8 GiB / 60 seconds of hashing, 4 GiB per whole CAK and 256 MiB per payload." :
                "Quick comparison: up to 256 MiB / 15 seconds of hashing, 64 MiB per whole CAK and 32 MiB per payload. Use Compare large CAKs for bigger files.");
            result.Notes.Add("Both modes: 256 archives, 200,000 indexed entries and 1,000 shared-file groups. Only top-level configured folders are scanned. Unsupported/encrypted metadata and uncompleted comparisons remain unverified.");
            if (result.Limited) result.Notes.Add("Inspection is partial: files or bytes omitted by limits, unsupported layouts or read errors are unverified. No matches is not proof that everything is different or healthy.");
            cancellationToken.ThrowIfCancellationRequested();
            return result;

            string State(string path) => active[path] ? "Enabled" : "Disabled";
            string Location(string path) => "[" + State(path) + "] " + path;
            string Activity(IEnumerable<string> paths)
            { int count = paths.Count(p => active[p]); int total = paths.Count(); return count + " enabled / " + (total - count) + " disabled"; }

            void Collect(string folder, bool modsFolder)
            {
                if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                { result.Limited = true; result.Notes.Add((modsFolder ? "Mods" : "Disabled") + " folder is missing or unavailable: " + (folder ?? "not configured")); return; }
                try
                {
                    int seen = 0;
                    foreach (string path in Directory.EnumerateFiles(folder).Take(MaxFolderEntries + 1).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (++seen > MaxFolderEntries) { result.Limited = true; result.Notes.Add("Folder-entry limit reached in " + folder); break; }
                        string name = Path.GetFileName(path);
                        if (name.IndexOf(".gorilla-", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                        bool enabled = modsFolder && name.EndsWith(".cak", StringComparison.OrdinalIgnoreCase);
                        bool archive = name.EndsWith(".cak", StringComparison.OrdinalIgnoreCase) || (modsFolder && includeDisabled && RenamedCak(name));
                        if (!archive || (!enabled && !includeDisabled)) continue;
                        string full = Path.GetFullPath(path);
                        if (active.ContainsKey(full)) continue;
                        if (active.Count >= MaxFiles) { result.Limited = true; result.Notes.Add("Archive limit reached; remaining CAKs were not scanned."); break; }
                        active.Add(full, enabled);
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException || ex is System.Security.SecurityException)
                { result.Limited = true; result.Notes.Add("Could not enumerate " + folder + ": " + ex.Message); }
            }
        }

        public static bool CharacterPath(string path)
        {
            string normalized = (path ?? "").Replace('\\', '/');
            return normalized.StartsWith("Characters/", StringComparison.OrdinalIgnoreCase) || normalized.StartsWith("Roster/", StringComparison.OrdinalIgnoreCase);
        }

        private static bool RenamedCak(string name) => Regex.IsMatch(name, @"^.+\.cak[^\\/]{1,24}$", RegexOptions.IgnoreCase) &&
            !new[] { ".tmp", ".part", ".crdownload", ".download", ".partial" }.Any(s => name.EndsWith(s, StringComparison.OrdinalIgnoreCase));
    }
}
