using System.Security.Cryptography;
using Gorilla.CrashDetails;
using Gorilla.Manager.Engine;

internal static class DuplicateScanTests
{
    public static void Run(string temp, Action<string, Action> test)
    {
        int number = 0;
        Places Case()
        {
            string root = Path.Combine(temp, "duplicate-tab-" + ++number);
            var places = new Places { ModsDir = Path.Combine(root, "mods"), OffDir = Path.Combine(root, "off") };
            Directory.CreateDirectory(places.ModsDir); Directory.CreateDirectory(places.OffDir);
            return places;
        }
        string Write(string folder, string name, CakFixture fixture = null)
        { string path = Path.Combine(folder, name); File.WriteAllBytes(path, (fixture ?? CakFixture.Create()).Bytes); return path; }

        test("duplicate tab: default scan excludes disabled, incomplete and nested files", () => {
            var p = Case(); Write(p.ModsDir, "a.cak"); Write(p.ModsDir, "b.CAK");
            Write(p.ModsDir, "third.cak.off"); Write(p.ModsDir, "pending.cak.partial"); Write(p.OffDir, "disabled.cak");
            Write(p.ModsDir, "temporary.gorilla-copy.cak");
            string nested = Path.Combine(p.ModsDir, "nested"); Directory.CreateDirectory(nested); Write(nested, "ignored.cak");
            var r = DuplicateScan.Run(p);
            Assert(r.Scanned == 2 && !r.IncludeDisabled);
            Has(r.Rows.Single(x => x.Kind == "Identical CAKs").Activity, "2 enabled / 0 disabled");
        });
        test("duplicate tab: disabled copies retain distinct full locations and activity", () => {
            var p = Case(); string enabled = Write(p.ModsDir, "same.cak"); string off = Write(p.OffDir, "same.cak");
            Write(p.ModsDir, "renamed.cak.disabled"); Write(p.ModsDir, "unfinished.cak.tmp");
            var r = DuplicateScan.Run(p, includeDisabled: true);
            Assert(r.Scanned == 3);
            var row = r.Rows.Single(x => x.Kind == "Identical CAKs"); Has(row.Activity, "1 enabled / 2 disabled");
            Has(row.Details, "[Enabled] " + enabled); Has(row.Details, "[Disabled] " + off);
            Has(row.Details, "does not form an active conflict");
        });
        test("duplicate tab: disabled archives alone are not labelled enabled", () => {
            var p = Case(); Write(p.OffDir, "a.cak"); Write(p.OffDir, "b.cak");
            var r = DuplicateScan.Run(p, includeDisabled: true);
            Has(r.Rows.Single(x => x.Kind == "Identical CAKs").Activity, "0 enabled / 2 disabled");
        });
        test("duplicate tab: overlapping configured folders are not double-counted", () => {
            var p = Case(); p.OffDir = p.ModsDir; Write(p.ModsDir, "a.cak");
            var r = DuplicateScan.Run(p, includeDisabled: true);
            Assert(r.Scanned == 1 && !r.Rows.Any(x => x.Kind == "Identical CAKs"));
        });
        test("duplicate tab: different character payloads can be filtered by path and owner", () => {
            var p = Case(); Write(p.ModsDir, "base.cak"); var changed = CakFixture.Create(); changed.Bytes[changed.TocEnd] ^= 1;
            Write(p.ModsDir, "alternative.cak", changed);
            var r = DuplicateScan.Run(p);
            var row = r.Filter("Different versions", "ALTERNATIVE.CAK", true).Single();
            Has(row.File, "Characters/1/test.bin"); Assert(row.CharacterRelated);
            Assert(!r.Filter("Identical CAKs", null, false).Any()); Assert(!r.Filter(null, "unrelated", true).Any());
            Assert(r.Filter("All results", "  characters/1  ", true).Any());
        });
        test("duplicate tab: same names with different bytes are not whole-file duplicates", () => {
            var p = Case(); Write(p.ModsDir, "same.cak"); var changed = CakFixture.Create(); changed.Bytes[changed.TocEnd] ^= 1;
            Write(p.OffDir, "same.cak", changed);
            var r = DuplicateScan.Run(p, includeDisabled: true);
            Assert(!r.Rows.Any(x => x.Kind == "Identical CAKs")); Assert(r.Rows.Any(x => x.Kind == "Different versions"));
        });
        test("duplicate tab: unsupported archives remain unverified, with full report notes", () => {
            var p = Case(); Write(p.ModsDir, "future.cak", CakFixture.Create(9, 9));
            var r = DuplicateScan.Run(p);
            Assert(r.Limited && r.Rows.Single().Kind == "Unverified");
            Has(r.FullText(), "future.cak"); Has(r.FullText(), "FDIR version 9.9"); Has(r.FullText(), "Inspection is partial");
        });
        test("duplicate tab: broken payloads expose the exact internal file", () => {
            var p = Case(); var broken = CakFixture.Create(); Array.Resize(ref broken.Bytes, broken.Bytes.Length - 1);
            Write(p.ModsDir, "broken.cak", broken);
            var row = DuplicateScan.Run(p).Rows.Single();
            Assert(row.Kind == "File problem" && row.CharacterRelated); Has(row.Details, "Characters/1/test.bin");
        });
        test("duplicate tab: missing folders are a partial scan, not a clean verdict", () => {
            var r = DuplicateScan.Run(new Places { ModsDir = Path.Combine(temp, "missing-folder") });
            Assert(r.Scanned == 0 && r.Limited); Has(r.FullText(), "missing or unavailable"); Has(r.FullText(), "not proof");
        });
        test("duplicate tab: archive-count limits are visible and bounded", () => {
            var p = Case();
            for (int i = 0; i < DuplicateScan.MaxFiles + 1; i++) Write(p.ModsDir, i.ToString("D3") + ".cak");
            var r = DuplicateScan.Run(p); Assert(r.Scanned == DuplicateScan.MaxFiles && r.Limited);
            Has(r.FullText(), "Archive limit reached");
        });
        test("duplicate tab: cancelling before work never publishes partial comparisons", () => {
            var p = Case(); Write(p.ModsDir, "a.cak");
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            Cancelled(() => DuplicateScan.Run(p, cancellationToken: cancellation.Token));
        });
        test("duplicate tab: cancellation between inventory and reading is respected", () => {
            var p = Case(); string path = Write(p.ModsDir, "a.cak"); byte[] before = File.ReadAllBytes(path);
            using var cancellation = new CancellationTokenSource();
            Cancelled(() => DuplicateScan.Run(p, cancellationToken: cancellation.Token, progress: new InlineProgress(_ => cancellation.Cancel())));
            Assert(before.SequenceEqual(File.ReadAllBytes(path)));
        });
        test("duplicate tab: larger-file mode compares archives above the quick-size limit", () => {
            var p = Case();
            foreach (string name in new[] { "large.cak", "copy.cak" })
            {
                string path = Write(p.ModsDir, name);
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Write); stream.SetLength(65L * 1024 * 1024);
            }
            var quick = DuplicateScan.Run(p); Assert(quick.Limited && !quick.Rows.Any(x => x.Kind == "Identical CAKs"));
            var deep = DuplicateScan.Run(p, largeFiles: true);
            Assert(deep.Rows.Single(x => x.Kind == "Identical CAKs").Details.Contains("Whole-file SHA-256"));
        });
        test("duplicate tab: comparisons leave bytes and timestamps unchanged", () => {
            var p = Case(); string path = Write(p.ModsDir, "a.cak"); Write(p.ModsDir, "b.cak");
            byte[] hash = SHA256.HashData(File.ReadAllBytes(path)); DateTime stamp = File.GetLastWriteTimeUtc(path);
            DuplicateScan.Run(p);
            Assert(hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))) && stamp == File.GetLastWriteTimeUtc(path));
        });
        test("duplicate tab: character filtering uses directory boundaries", () => {
            Assert(DuplicateScan.CharacterPath(@"characters\123\model.bin")); Assert(DuplicateScan.CharacterPath("Roster/entry.bin"));
            Assert(!DuplicateScan.CharacterPath("SomeCharacters/model.bin")); Assert(!DuplicateScan.CharacterPath("Characters-old/model.bin"));
        });
        test("crash diagnosis: Parsec-named fault is a clue, never a confirmed cause", () => {
            var evidence = new CrashEvidence { When = DateTime.Now, Module = "parsec64.dll", Code = "c0000005" };
            var d = Diagnosis.Explain(evidence, null, new[] { evidence });
            Has(d.Summary, "Parsec-named"); Has(d.Confidence, "not established"); Has(d.NextStep, "one guest controller at a time");
            Has(d.NextStep, "locally with Parsec fully closed");
            evidence.Module = "KERNELBASE.dll";
            Assert(!Diagnosis.Explain(evidence, null, new[] { evidence }).Summary.Contains("Parsec"));
        });
        test("comparison: cancellation propagates through byte comparison", () => {
            var p = Case(); var a = CakInspector.Inspect(Write(p.ModsDir, "a.cak")); var b = CakInspector.Inspect(Write(p.ModsDir, "b.cak"));
            using var cancellation = new CancellationTokenSource();
            IEnumerable<CakInspection> Inputs() { yield return a; cancellation.Cancel(); yield return b; }
            Cancelled(() => CakComparison.Compare(Inputs(), cancellationToken: cancellation.Token));
        });
    }

    private static void Assert(bool value) { if (!value) throw new Exception("Duplicate tab assertion failed."); }
    private static void Has(string value, string text) => Assert(value != null && value.Contains(text, StringComparison.OrdinalIgnoreCase));
    private static void Cancelled(Action action)
    { try { action(); } catch (OperationCanceledException) { return; } throw new Exception("Expected cancellation."); }
    private sealed class InlineProgress : IProgress<string>
    {
        private readonly Action<string> action;
        public InlineProgress(Action<string> action) { this.action = action; }
        public void Report(string value) => action(value);
    }
}
