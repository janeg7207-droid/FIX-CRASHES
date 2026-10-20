using Gorilla.CrashDetails;
using Gorilla.Manager.Engine;

internal static class CakComparisonTests
{
    public static void Run(string temp, Action<string, Action> test)
    {
        string root = Path.Combine(temp, "comparison-tests"); Directory.CreateDirectory(root);
        int caseNumber = 0;
        string Case() { string p = Path.Combine(root, (++caseNumber).ToString()); Directory.CreateDirectory(p); return p; }
        CakInspection Write(string folder, string name, CakFixture fixture)
        { string path = Path.Combine(folder, name); File.WriteAllBytes(path, fixture.Bytes); return CakInspector.Inspect(path); }

        test("comparison: whole-archive duplicates are named with SHA-256 proof", () => {
            string dir = Case(); var a = Write(dir, "first.cak", CakFixture.Create()); var b = Write(dir, "second.cak", CakFixture.Create());
            var result = CakComparison.Compare(new[] { a, b });
            Assert(result.IdenticalArchives.Single().Names.Count == 2);
            Has(result.FullText(), "first.cak"); Has(result.FullText(), "second.cak");
            Assert(result.IdenticalArchives[0].Sha256.Length == 64);
        });
        test("comparison: identical stored files are distinguished from different containers", () => {
            string dir = Case(); var f = CakFixture.Create(); f.U32(80, 123);
            var result = CakComparison.Compare(new[] { Write(dir, "a.cak", CakFixture.Create()), Write(dir, "b.cak", f) });
            Assert(result.IdenticalArchives.Count == 0);
            var group = result.SharedFiles.Single();
            Assert(group.AllCompared && !group.DifferentStoredBytes);
            Assert(group.Copies.Select(c => c.Sha256).Distinct().Count() == 1);
            Has(group.Text(), "Identical stored bytes"); Has(group.Text(), "Characters/1/test.bin");
        });
        test("comparison: changed payload identifies different versions of the exact internal path", () => {
            string dir = Case(); var f = CakFixture.Create(); f.Bytes[f.TocEnd + 3] ^= 1;
            var result = CakComparison.Compare(new[] { Write(dir, "a.cak", CakFixture.Create()), Write(dir, "b.cak", f) });
            var group = result.SharedFiles.Single(); Assert(group.DifferentStoredBytes);
            Has(group.Text(), "Different stored versions"); Has(group.Text(), "a.cak"); Has(group.Text(), "b.cak");
            Has(group.Text(), "not a proven conflict or crash cause");
        });
        test("comparison: three archives retain all owners and matching subsets", () => {
            string dir = Case(); var changed = CakFixture.Create(); changed.Bytes[changed.TocEnd] ^= 1;
            var group = CakComparison.Compare(new[] { Write(dir, "a.cak", CakFixture.Create()), Write(dir, "b.cak", CakFixture.Create()), Write(dir, "c.cak", changed) }).SharedFiles.Single();
            Assert(group.Copies.Count == 3 && group.DifferentStoredBytes);
            Has(group.Text(), "Matching stored-byte copies: a.cak, b.cak");
            Has(group.Text(), "c.cak -> Characters/1/test.bin");
        });
        test("comparison: same basename at different paths is not a duplicate file", () => {
            string dir = Case();
            var result = CakComparison.Compare(new[] { Write(dir, "a.cak", CakFixture.Create(names: new[] { "A/test.bin" })), Write(dir, "b.cak", CakFixture.Create(names: new[] { "B/test.bin" })) });
            Assert(result.SharedFiles.Count == 0 && result.IdenticalArchives.Count == 0);
        });
        test("comparison: a shared ID with different path names is not called the same file", () => {
            string dir = Case(); var a = CakFixture.Create(); var b = CakFixture.Create(names: new[] { "different.bin" });
            Array.Copy(a.Bytes, a.FileLookup, b.Bytes, b.FileLookup, 8);
            var group = CakComparison.Compare(new[] { Write(dir, "a.cak", a), Write(dir, "b.cak", b) }).SharedFiles.Single();
            Assert(!group.SamePath && !group.DifferentStoredBytes && !group.AllCompared);
            Has(group.Status, "identity unconfirmed");
        });
        test("comparison: encrypted data is identified as stored ciphertext", () => {
            string dir = Case(); var encrypted = CakFixture.Create(); encrypted.U32(encrypted.FileInfo + 30, 0x100);
            var group = CakComparison.Compare(new[] { Write(dir, "plain.cak", CakFixture.Create()), Write(dir, "encrypted.cak", encrypted) }).SharedFiles.Single();
            Has(group.Text(), "ciphertext"); Has(group.Text(), "Storage metadata differs");
            Has(group.Text(), "decoded content is unverified");
        });
        test("comparison: byte limits produce unverified results, not false equality", () => {
            string dir = Case();
            var result = CakComparison.Compare(new[] { Write(dir, "a.cak", CakFixture.Create()), Write(dir, "b.cak", CakFixture.Create()) }, byteBudget: 1);
            Assert(result.Limited && result.BytesRead <= 1);
            var group = result.SharedFiles.Single(); Assert(!group.AllCompared && !group.DifferentStoredBytes);
            Has(group.Status, "comparison incomplete");
        });
        test("comparison: time limits never imply equal payloads", () => {
            string dir = Case();
            var result = CakComparison.Compare(new[] { Write(dir, "a.cak", CakFixture.Create()), Write(dir, "b.cak", CakFixture.Create()) }, timeBudgetMilliseconds: 0);
            Assert(result.Limited && result.BytesRead == 0 && !result.SharedFiles.Single().AllCompared);
        });
        test("comparison: a file changed after table inspection is not trusted", () => {
            string dir = Case(); var a = Write(dir, "a.cak", CakFixture.Create()); var b = Write(dir, "b.cak", CakFixture.Create());
            byte[] bytes = File.ReadAllBytes(b.FilePath); bytes[bytes.Length - 1] ^= 1; File.WriteAllBytes(b.FilePath, bytes);
            File.SetLastWriteTimeUtc(b.FilePath, DateTime.UtcNow.AddSeconds(2));
            var result = CakComparison.Compare(new[] { a, b });
            Assert(result.IdenticalArchives.Count == 0 && !result.SharedFiles.Single().AllCompared);
            Has(result.SharedFiles[0].Text(), "changed since its tables were read");
        });
        test("comparison: external-reference entries are not read as in-archive bytes", () => {
            string dir = Case(); var external = CakFixture.Create(); external.Bytes[7] = 5; external.U64(external.FileInfo + 16, 0x1000000);
            var group = CakComparison.Compare(new[] { Write(dir, "a.cak", CakFixture.Create()), Write(dir, "external.cak", external) }).SharedFiles.Single();
            Assert(!group.AllCompared); Has(group.Text(), "external-reference archive");
        });
        test("comparison: a corrupt payload range is rejected without reading outside the file", () => {
            string dir = Case(); var a = Write(dir, "a.cak", CakFixture.Create()); var b = Write(dir, "b.cak", CakFixture.Create());
            b.Assets[0].StoredLength = ulong.MaxValue;
            var group = CakComparison.Compare(new[] { a, b }).SharedFiles.Single();
            Assert(!group.AllCompared); Has(group.Text(), "range outside the archive");
        });
        test("comparison: scanning does not change CAK bytes or modification time", () => {
            string dir = Case(); var a = Write(dir, "a.cak", CakFixture.Create()); var b = Write(dir, "b.cak", CakFixture.Create());
            byte[] before = File.ReadAllBytes(a.FilePath); long ticks = File.GetLastWriteTimeUtc(a.FilePath).Ticks;
            CakComparison.Compare(new[] { a, b });
            Assert(before.SequenceEqual(File.ReadAllBytes(a.FilePath)) && ticks == File.GetLastWriteTimeUtc(a.FilePath).Ticks);
        });
        test("comparison: detailed report lists all named archive copies and internal paths", () => {
            string dir = Case(); Write(dir, "a.cak", CakFixture.Create()); Write(dir, "b.cak", CakFixture.Create()); Write(dir, "c.cak", CakFixture.Create());
            string data = Path.Combine(dir, "data"); var report = new Report();
            CakChecks.Append(report, new Places { ModsDir = dir, GameDir = dir, DataDir = data });
            string text = File.ReadAllText(Path.Combine(data, "Crash Reports", "Same files report.txt"));
            Has(text, "a.cak"); Has(text, "b.cak"); Has(text, "c.cak"); Has(text, "Characters/1/test.bin"); Has(text, "Stored SHA-256");
            Assert(report.Findings.Single(f => f.Id == "gorilla-cak-summary").Folder == Path.Combine(data, "Crash Reports"));
        });
        test("crash leads: a malformed payload names the precise archive and internal asset", () => {
            string dir = Case(); var broken = CakFixture.Create(); Array.Resize(ref broken.Bytes, broken.Bytes.Length - 1);
            var inspection = Write(dir, "broken.cak", broken);
            Assert(inspection.Issues.Single(i => i.Error).AssetPath == "Characters/1/test.bin");
            var report = new Report(); CakChecks.Append(report, new Places { ModsDir = dir, GameDir = dir });
            var lead = report.Findings.Single(f => f.Id == "gorilla-cak-crash-leads");
            Has(lead.Title, "broken.cak -> Characters/1/test.bin"); Has(lead.Text, "connection to the game crash remains unconfirmed");
        });
        test("crash leads: identical copies alone do not become a bad-mod verdict", () => {
            string dir = Case(); Write(dir, "a.cak", CakFixture.Create()); Write(dir, "b.cak", CakFixture.Create());
            var report = new Report(); CakChecks.Append(report, new Places { ModsDir = dir, GameDir = dir });
            Has(report.Findings.Single(f => f.Id == "gorilla-cak-crash-leads").Title, "no file-specific cause established");
        });
        test("crash leads: fresh loader errors are quoted, zero-error counters are ignored", () => {
            string dir = Case(); var file = Write(dir, "a.cak", CakFixture.Create()); string plugins = Path.Combine(dir, "Plugins"); Directory.CreateDirectory(plugins);
            string log = Path.Combine(plugins, "cakehook_load_report.txt");
            File.WriteAllText(log, "load report (new session)\nloaded a.cak; failed=0\na.cak: no failures\n");
            Assert(CakLoaderLog.Read(dir).RecentErrorForFile(file.FilePath, DateTime.Now.AddSeconds(1)) == null);
            File.WriteAllText(log, "load report (new session)\nfailed to read a.cak\n");
            Has(CakLoaderLog.Read(dir).RecentErrorForFile(file.FilePath, DateTime.Now.AddSeconds(1)), "failed to read a.cak");
        });
        test("crash leads: unmarked or stale logs do not become current error evidence", () => {
            string dir = Case(); var file = Write(dir, "a.cak", CakFixture.Create()); string plugins = Path.Combine(dir, "Plugins"); Directory.CreateDirectory(plugins);
            string log = Path.Combine(plugins, "cakehook_load_report.txt"); File.WriteAllText(log, "failed to read a.cak");
            Assert(CakLoaderLog.Read(dir).RecentErrorForFile(file.FilePath, DateTime.Now.AddSeconds(1)) == null);
            File.WriteAllText(log, "load report (new session)\nfailed to read a.cak"); File.SetLastWriteTimeUtc(log, DateTime.UtcNow.AddDays(-1));
            Assert(CakLoaderLog.Read(dir).RecentErrorForFile(file.FilePath, DateTime.Now.AddSeconds(1)) == null);
        });
        test("crash leads: mod-search results name an archive but do not invent an inner culprit", () => {
            var report = new Report(); CrashLeads.AppendSearchResult(report, new FinderState { Active = true, Result = "baked.cak", Round = 3 });
            var finding = report.Findings.Single(); Has(finding.Title, "baked.cak"); Has(finding.Text, "3 test round");
            Has(finding.Text, "does not identify a particular internal file");
            CrashLeads.AppendSearchResult(report, new FinderState { Active = true, Result = "none", Round = 4 });
            Assert(report.Findings.Count == 1); Has(report.Findings[0].Title, "did not isolate one mod");
        });
        test("crash leads: inactive search history is not presented as a current result", () => {
            var report = new Report(); CrashLeads.AppendSearchResult(report, new FinderState { Active = false, Result = "old.cak" });
            Assert(report.Findings.Count == 0);
        });
    }

    private static void Assert(bool value, string explanation = "") { if (!value) throw new Exception("Comparison assertion failed. " + explanation); }
    private static void Has(string value, string text) => Assert(value != null && value.Contains(text, StringComparison.OrdinalIgnoreCase), "Expected '" + text + "' in '" + value + "'.");
}
