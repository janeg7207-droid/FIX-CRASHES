using Gorilla.CrashDetails;
using Gorilla.Manager.Engine;

internal static class CakTests
{
    public static void Run(string temp, Action<string, Action> test)
    {
        string root = Path.Combine(temp, "cak-tests"); Directory.CreateDirectory(root);
        CakInspection Inspect(CakFixture fixture, string name = "sample.cak")
        { string path = Path.Combine(root, name); File.WriteAllBytes(path, fixture.Bytes); return CakInspector.Inspect(path); }

        test("CAK: actual supported tables and internal paths are read", () => {
            var result = Inspect(CakFixture.Create());
            Assert(result.TablesRead && !result.HasErrors);
            Assert(result.Assets.Single().Path == "Characters/1/test.bin");
            Assert(result.Assets[0].StoredBytes == 64);
            Has(result.Text(), "not established by this scan");
        });
        test("CAK: all documented plaintext layouts are readable", () => {
            foreach (var v in new[] { (6, 7), (6, 8), (8, 1), (8, 2), (8, 3), (8, 7), (9, 1), (9, 2), (9, 3) })
            {
                var result = Inspect(CakFixture.Create((byte)v.Item1, (byte)v.Item2));
                Assert(result.TablesRead && !result.HasErrors, v.ToString() + ": " + result.Text());
                Assert(result.Assets[0].Path == "Characters/1/test.bin");
            }
        });
        test("CAK: ZIP renamed to CAK is identified", () => {
            string path = Path.Combine(root, "renamed.cak"); File.WriteAllBytes(path, new byte[] { 80, 75, 3, 4, 0, 0, 0, 0 });
            var result = CakInspector.Inspect(path); Assert(result.HasErrors); Has(result.Text(), "ZIP archive");
        });
        test("CAK: zero-byte and short headers are diagnosed", () => {
            string path = Path.Combine(root, "empty.cak"); File.WriteAllBytes(path, Array.Empty<byte>());
            Assert(CakInspector.Inspect(path).Issues.Any(i => i.Code == "Empty"));
            File.WriteAllBytes(path, new byte[] { 70, 68, 73, 82 });
            Assert(CakInspector.Inspect(path).Issues.Any(i => i.Code == "Truncated"));
        });
        test("CAK: unsupported version is limited rather than damaged", () => {
            var f = CakFixture.Create(); f.Bytes[5] = 9;
            var result = Inspect(f); Assert(!result.HasErrors && !result.TablesRead);
            Assert(result.Version == "9.9"); Has(result.Text(), "NOT been validated");
        });
        test("CAK: encrypted metadata is not mislabeled as corrupt", () => {
            var f = CakFixture.Create(); f.Bytes[7] |= 0x80;
            var result = Inspect(f); Assert(result.Encrypted && !result.TablesRead && !result.HasErrors);
            Has(result.Text(), "original filename");
        });
        test("CAK: legacy metadata encryption flag is recognized", () => {
            var f = CakFixture.Create(6, 8); f.Bytes[7] |= 0x40;
            Assert(Inspect(f).Encrypted);
        });
        test("CAK: a truncated file payload is found", () => {
            var f = CakFixture.Create(); Array.Resize(ref f.Bytes, f.Bytes.Length - 10);
            var result = Inspect(f); Assert(result.HasErrors); Has(result.Text(), "Payload");
        });
        test("CAK: corrupt table section offsets are rejected", () => {
            var f = CakFixture.Create(); f.U32(28, (uint)f.Bytes.Length + 100);
            var result = Inspect(f); Assert(result.HasErrors); Has(result.Text(), "section");
        });
        test("CAK: too-small lookup table is rejected", () => {
            var f = CakFixture.Create(); f.U32(32, 1);
            Assert(Inspect(f).HasErrors);
        });
        test("CAK: out-of-range lookup index is rejected", () => {
            var f = CakFixture.Create(); f.U32(f.FileLookup + 8, 99);
            var result = Inspect(f); Assert(result.HasErrors); Has(result.Text(), "lookup index");
        });
        test("CAK: repeated file hashes inside one archive are rejected", () => {
            var f = CakFixture.Create(names: new[] { "a.bin", "b.bin" });
            Array.Copy(f.Bytes, f.FileLookup, f.Bytes, f.FileLookup + 12, 8);
            Assert(Inspect(f).HasErrors);
        });
        test("CAK: external-reference archive does not need in-file payloads", () => {
            var f = CakFixture.Create(); f.Bytes[7] = 5; f.U64(f.FileInfo + 16, 0x10000000);
            var result = Inspect(f); Assert(result.External && result.TablesRead && !result.HasErrors);
            Has(result.Text(), "outside the archive");
        });
        test("CAK: parent directory and name offsets are checked", () => {
            var f = CakFixture.Create(); f.U32(f.FileInfo + 4, 50); Assert(Inspect(f).HasErrors);
            f = CakFixture.Create(); f.U32(f.FileInfo, 10000000); Assert(Inspect(f).HasErrors);
        });
        test("CAK: traversal names are identified without extraction", () => {
            var result = Inspect(CakFixture.Create(names: new[] { "../bad.bin" }));
            Assert(result.HasErrors); Has(result.Text(), "parent-traversal");
        });
        test("CAK: hostile counts are bounded without a corruption claim", () => {
            var f = CakFixture.Create(); f.U32(8, uint.MaxValue);
            var result = Inspect(f); Assert(!result.TablesRead && !result.HasErrors);
            Has(result.Text(), "safety limit");
        });
        test("CAK: chunk metadata is read and count mismatches are found", () => {
            var f = CakFixture.Create(chunks: true); Assert(Inspect(f).TablesRead);
            f.U32(16, 2); Assert(Inspect(f).HasErrors);
        });
        test("CAK: chunk bounds use the last chunk endpoint", () => {
            var f = CakFixture.Create(chunks: true); f.U32(f.FileInfo + 8, 99999);
            Assert(Inspect(f).TablesRead); // published builder caveat: stored size can be stale
            f.U32(f.FileInfo + 34, 99999); Assert(Inspect(f).HasErrors);
        });
        test("CAK: expanded-size payload flag is respected", () => {
            var f = CakFixture.Create(); f.U32(f.FileInfo + 8, 99999); f.U32(f.FileInfo + 30, 0x80000000);
            var result = Inspect(f); Assert(result.TablesRead && !result.HasErrors);
            f.U32(f.FileInfo + 24, 99999); Assert(Inspect(f).HasErrors);
        });
        test("CAK: missing files return a useful read error", () => {
            var result = CakInspector.Inspect(Path.Combine(root, "missing.cak"));
            Assert(result.HasErrors); Has(result.Text(), "could not be read");
        });
        test("CAK: a structural scan leaves original bytes unchanged", () => {
            var f = CakFixture.Create(); string path = Path.Combine(root, "untouched.cak");
            File.WriteAllBytes(path, f.Bytes); byte[] original = File.ReadAllBytes(path);
            CakInspector.Inspect(path); Assert(original.SequenceEqual(File.ReadAllBytes(path)));
        });
        test("CAK: enabled archives sharing assets get a cautious overlap finding", () => {
            string mods = Path.Combine(root, "overlap"); Directory.CreateDirectory(mods);
            File.WriteAllBytes(Path.Combine(mods, "one.cak"), CakFixture.Create().Bytes);
            File.WriteAllBytes(Path.Combine(mods, "two.CAK"), CakFixture.Create().Bytes);
            var report = new Report(); CakChecks.Append(report, new Places { ModsDir = mods, GameDir = root });
            var overlap = report.Findings.Single(f => f.Id.StartsWith("gorilla-cak-overlap-"));
            Has(overlap.Text, "intentional replacement"); Assert(!overlap.Serious);
            Has(report.Findings.Single(f => f.Id == "gorilla-cak-summary").Text, "2 enabled");
        });
        test("CAK: disabled files are not blamed for current enabled content", () => {
            string mods = Path.Combine(root, "disabled"); Directory.CreateDirectory(mods);
            File.WriteAllBytes(Path.Combine(mods, "one.cak"), CakFixture.Create().Bytes);
            File.WriteAllBytes(Path.Combine(mods, "two.cak.off"), Array.Empty<byte>());
            var report = new Report(); CakChecks.Append(report, new Places { ModsDir = mods, GameDir = root });
            Assert(!report.Findings.Any(f => f.Serious));
            Has(report.Findings.Single(f => f.Id == "gorilla-cak-summary").Text, "1 enabled");
        });
        test("CAK: corrupt enabled file gets a file-specific actionable card", () => {
            string mods = Path.Combine(root, "broken"); Directory.CreateDirectory(mods);
            File.WriteAllBytes(Path.Combine(mods, "broken.cak"), Array.Empty<byte>());
            var report = new Report(); CakChecks.Append(report, new Places { ModsDir = mods, GameDir = root });
            var card = report.Findings.Single(f => f.Id.StartsWith("gorilla-cak-file-"));
            Assert(card.Serious && card.Title.Contains("broken.cak")); Has(card.Text, "empty");
            Assert(report.Mood == "stop");
            CakChecks.Append(report, new Places { ModsDir = mods, GameDir = root });
            Assert(report.Findings.Count(f => f.Id == "gorilla-cak-summary") == 1);
            Assert(report.Looked.Count(s => s.StartsWith("CAK inspection:")) == 1);
        });
        test("CAK: absent mods folder is explained without changing anything", () => {
            string path = Path.Combine(root, "not-created"); var report = new Report();
            CakChecks.Append(report, new Places { ModsDir = path, GameDir = root });
            Has(report.Findings.Last().Text, "No enabled"); Assert(!Directory.Exists(path));
        });
        test("CAK: loader evidence uses the last session and exact filename", () => {
            string game = Path.Combine(root, "loader"); string plugins = Path.Combine(game, "Plugins"); Directory.CreateDirectory(plugins);
            string cak = Path.Combine(root, "my.cak"); File.WriteAllBytes(cak, CakFixture.Create().Bytes);
            File.WriteAllText(Path.Combine(plugins, "cakehook_load_report.txt"),
                "load report (new session)\nold failure my.cak\nload report (new session)\nloaded my.cak\nfailed not-my.cak\nfailed my.cak.off\n");
            string evidence = CakLoaderLog.Read(game).ForFile(cak);
            Has(evidence, "loaded my.cak"); Assert(!evidence.Contains("old failure") && !evidence.Contains("not-my.cak") && !evidence.Contains("my.cak.off"));
            Has(evidence, "not an inferred rejection");
        });
        test("CAK: stale loader log is not evidence about a newer copy", () => {
            string game = Path.Combine(root, "stale"); string plugins = Path.Combine(game, "Plugins"); Directory.CreateDirectory(plugins);
            string cak = Path.Combine(root, "stale.cak"); File.WriteAllBytes(cak, CakFixture.Create().Bytes);
            string log = Path.Combine(plugins, "cakehook_load_report.txt"); File.WriteAllText(log, "failure stale.cak");
            File.SetLastWriteTimeUtc(log, DateTime.UtcNow.AddDays(-1));
            Has(CakLoaderLog.Read(game).ForFile(cak), "predates the current CAK");
        });
        test("CAK: missing loader evidence does not imply a failed load", () => {
            var log = CakLoaderLog.Read(Path.Combine(root, "no-log"));
            Has(log.Status, "does not prove"); Assert(log.ForFile(Path.Combine(root, "some.cak")) == null);
        });
    }

    private static void Assert(bool value, string explanation = "") { if (!value) throw new Exception("CAK assertion failed. " + explanation); }
    private static void Has(string value, string text) => Assert(value != null && value.Contains(text, StringComparison.OrdinalIgnoreCase), "Expected " + text + " in " + value);
}
