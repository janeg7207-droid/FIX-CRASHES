using System.Reflection;
using System.Runtime.CompilerServices;
using Gorilla.CrashDetails;
using Gorilla.Manager.Engine;

int passed = 0;
int failed = 0;
var now = new DateTime(2026, 10, 18, 12, 0, 0);
string temp = Path.Combine(Path.GetTempPath(), "gorilla-crash-details-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);
try
{
    Test("access violation explains failure without claiming a bad mod", () => {
        var d = Explain("c0000005");
        Contains(d.Explanation, "access violation");
        Contains(d.Confidence, "Unknown root cause");
        Contains(d.Explanation, "cannot name a mod");
    });
    Test("plugin path gives a lead, not proof", () => {
        var d = Explain("0xc0000005", @"C:\WWE\Plugins\OldRing.dlp");
        Contains(d.Summary, "OldRing.dlp");
        Contains(d.Confidence, "unconfirmed");
        Contains(d.NextStep, "restored");
    });
    Test("plugin path matching respects directory boundaries", () => {
        Assert(!Diagnosis.IsInsidePlugins(@"C:\WWE2\Plugins\old.dlp", @"C:\WWE"));
        Assert(!Diagnosis.IsInsidePlugins(@"C:\WWE\Plugins-old\old.dlp", @"C:\WWE"));
        Assert(!Diagnosis.IsInsidePlugins(@"C:\WWE\Plugins\..\system.dll", @"C:\WWE"));
        Assert(Diagnosis.IsInsidePlugins(@"c:/wwe/Plugins/test.dlp", @"C:\WWE\"));
    });
    Test("CakeHook is kept enabled during mod isolation", () => {
        var d = Explain("c0000005", @"C:\WWE\Plugins\CakeHook.dlp");
        Contains(d.NextStep, "Keep CakeHook enabled");
        Contains(d.NextStep, "does not identify a bad mod");
    });
    Test("a hang is not misreported as a crash exception", () => {
        var c = Evidence("c0000005", @"C:\WWE\Plugins\OldRing.dlp");
        c.Froze = true;
        var d = Diagnosis.Explain(c, @"C:\WWE", new[] { c });
        Contains(d.Summary, "stopped responding");
        Contains(d.Evidence, "not supplied for this hang");
        Assert(!d.Explanation.Contains("access violation"));
    });
    Test("driver fault does not prove a broken driver", () => {
        var d = Explain("c0000005", @"C:\Windows\System32\nvwgf2umx.dll");
        Contains(d.Summary, "Graphics");
        Contains(d.Explanation, "does not prove");
        Contains(d.NextStep, "overlays");
    });
    Test("shared Windows DLL does not get blamed", () => {
        var d = Explain("e06d7363", @"C:\Windows\System32\KERNELBASE.dll");
        Contains(d.Explanation, "does not mean Windows caused");
        Contains(d.Explanation, "C++ exception");
    });
    Test("fail-fast is not always a stack buffer overrun", () => {
        Contains(Explain("c0000409").Explanation, "does not by itself prove a stack buffer overrun");
    });
    Test("heap corruption can originate elsewhere", () => {
        Contains(Explain("c0000374").Explanation, "may differ");
    });
    Test("out-of-memory explains allocation without diagnosing bad RAM", () => {
        var d = Explain("c0000017");
        Contains(d.Summary, "allocation");
        Contains(d.NextStep, "does not by itself prove a defective RAM");
    });
    Test("loader error recommends official dependencies", () => {
        var d = Explain("c000007b");
        Contains(d.Summary, "could not load");
        Contains(d.NextStep, "official publishers");
    });
    Test("all supported error codes have a specific explanation", () => {
        foreach (string code in new[] { "c0000005", "c0000409", "c0000374", "e06d7363", "e0434352", "c00000fd", "c000001d", "c0000094", "c0000017", "8007000e", "c0000006", "c0000135", "c000007b", "c0000142", "80000003", "887a0005", "887a0006", "887a0007" })
            Assert(!Explain(code).Explanation.Contains("no specific interpretation"), code);
    });
    Test("unknown and missing codes stay unknown", () => {
        Contains(Explain("0x12345678").Explanation, "no specific interpretation");
        Contains(Explain(null).Explanation, "did not provide");
        Assert(Diagnosis.NormalizeCode(" 0xc0000005 ") == "0xC0000005");
    });
    Test("repeat count includes code and excludes older/different failures", () => {
        var c = Evidence("c0000005", @"C:\WWE\WWE2K26_x64.exe");
        var same = Evidence("0xC0000005", @"C:\WWE\WWE2K26_x64.exe"); same.When = now.AddDays(-1);
        var otherCode = Evidence("c0000409", c.ModulePath);
        var old = Evidence(c.Code, c.ModulePath); old.When = now.AddDays(-8);
        var d = Diagnosis.Explain(c, @"C:\WWE", new[] { c, same, otherCode, old });
        Contains(d.Evidence, "preceding 7 days: 2");
    });
    Test("missing module/offset does not claim repeated identical crashes", () => {
        var c = Evidence(null, null); c.Offset = null; c.Module = null;
        var d = Diagnosis.Explain(c, @"C:\WWE", new[] { c, c });
        Assert(!d.Evidence.Contains("Matching"));
        Contains(d.Evidence, "not recorded");
    });

    // Integration checks use the actual Report/Finding/Core classes from the
    // supplied executable, not re-created copies of those models.
    var core = (Core)RuntimeHelpers.GetUninitializedObject(typeof(Core));
    string game = Path.Combine(temp, "game");
    Directory.CreateDirectory(game);
    File.WriteAllBytes(Path.Combine(game, "WWE2K26_x64.exe"), new byte[] { 0 });
    var places = new Places { GameDir = game, DataDir = Path.Combine(temp, "data") };
    typeof(Core).GetProperty("Places", BindingFlags.Public | BindingFlags.Instance).SetValue(core, places);
    CrashLog.Readable = true;
    Test("report enhancement preserves existing fixes and suspects", () => {
        int clicked = 0;
        var finding = new Finding { Id = "existing-fix", Title = "Existing check", Text = "Original", FixLabel = "Fix", Fix = () => { clicked++; return "done"; } };
        var suspect = new Suspect { Title = "Recent mod" };
        var report = RecentReport(); report.Findings.Add(finding); report.Suspects.Add(suspect);
        Assert(ReferenceEquals(CrashDetailsBridge.Enhance(report, core, () => now), report));
        Assert(report.Findings.Contains(finding)); Assert(report.Suspects.Contains(suspect));
        Assert(finding.Fix() == "done" && clicked == 1);
        Contains(report.Findings[0].Text, "Confidence:");
    });
    Test("enhanced report is saved locally with an Open folder target", () => {
        var report = RecentReport();
        CrashDetailsBridge.Enhance(report, core, () => now);
        var finding = report.Findings[0];
        Assert(Directory.Exists(finding.Folder));
        string text = File.ReadAllText(Path.Combine(finding.Folder, "Latest crash report.txt"));
        Contains(text, "0xC0000005"); Contains(text, "What to try next:");
        Contains(text, "saved locally only");
    });
    Test("refresh does not duplicate the details card", () => {
        var report = RecentReport();
        CrashDetailsBridge.Enhance(report, core, () => now);
        CrashDetailsBridge.Enhance(report, core, () => now);
        Assert(report.Findings.Count(f => f.Id == "gorilla-crash-details") == 1);
    });
    Test("unreadable event log is not a healthy-game diagnosis", () => {
        CrashLog.Readable = false;
        try {
            var report = new Report { Mood = "good", Headline = "Nothing broken" };
            CrashDetailsBridge.Enhance(report, core, () => now);
            Assert(report.Mood != "good");
            Contains(report.Findings[0].Text, "not evidence");
            Contains(report.Line, "unknown");
        } finally { CrashLog.Readable = true; }
    });
    Test("no crash record explicitly leaves the cause unknown", () => {
        var report = new Report { Mood = "good" };
        CrashDetailsBridge.Enhance(report, core, () => now);
        Assert(report.Mood == "look");
        Contains(report.Headline, "No crash cause");
        Contains(report.Findings[0].Text, "does not prove");
    });
    Test("old crash is labeled historical and does not become a current alarm", () => {
        var report = RecentReport(); report.Mood = "look"; report.Headline = "Current check";
        report.Crashes[0].When = now.AddDays(-12);
        CrashDetailsBridge.Enhance(report, core, () => now);
        Assert(report.Headline == "Current check");
        Contains(report.Findings[0].Text, "Historical record");
        Assert(!report.Findings[0].Serious);
    });
    Test("missing application path is disclosed", () => {
        var report = RecentReport(); report.Crashes[0].AppPath = null;
        CrashDetailsBridge.Enhance(report, core, () => now);
        Contains(report.Findings[0].Text, "installation could not be independently confirmed");
    });
    Test("report write failure leaves the explanation and original report usable", () => {
        string old = places.DataDir;
        string blocked = Path.Combine(temp, "not-a-directory"); File.WriteAllText(blocked, "test"); places.DataDir = blocked;
        try {
            var report = RecentReport();
            CrashDetailsBridge.Enhance(report, core, () => now);
            Contains(report.Findings[0].Text, "could not be saved");
            Contains(report.Findings[0].Text, "Windows recorded:");
            Assert(report.Findings[0].Folder == null);
        } finally { places.DataDir = old; }
    });
    Test("plugin certainty is corrected without removing its action", () => {
        var report = RecentReport();
        var finding = new Finding { Id = "crash-in-old.dlp", Text = "Windows recorded the crash in this plugin's own code, so the plugin is the cause.", Fix = () => "off" };
        report.Findings.Add(finding);
        CrashDetailsBridge.Enhance(report, core, () => now);
        Contains(finding.Text, "not a confirmed root cause"); Assert(finding.Fix() == "off");
    });
    Test("a failing clock does not discard the original checks", () => {
        var report = RecentReport(); var existing = new Finding { Id = "existing" }; report.Findings.Add(existing);
        var result = CrashDetailsBridge.Enhance(report, core, () => throw new IOException("clock fixture"));
        Assert(ReferenceEquals(report, result)); Assert(report.Findings.Contains(existing));
        Assert(report.Findings.Any(f => f.Id == "gorilla-crash-details-error"));
    });
    Test("a missing game leaves the original folder selection flow intact", () => {
        var report = new Report { Headline = "Find game" };
        string saved = places.GameDir; places.GameDir = Path.Combine(temp, "missing");
        try { Assert(ReferenceEquals(CrashDetailsBridge.Enhance(report, core, () => now), report)); Assert(report.Findings.Count == 0); }
        finally { places.GameDir = saved; }
    });
    CakTests.Run(temp, Test);
    CakComparisonTests.Run(temp, Test);
    DuplicateScanTests.Run(temp, Test);
}
finally { Directory.Delete(temp, true); }
Console.WriteLine($"{passed} passed, {failed} failed; {passed + failed} tests executed.");
return failed == 0 ? 0 : 1;

CrashEvidence Evidence(string code, string path) => new CrashEvidence { When = now.AddMinutes(-5), Code = code, ModulePath = path, Module = "WWE2K26_x64.exe", Offset = "00001234", AppPath = @"C:\WWE\WWE2K26_x64.exe" };
Diagnosis Explain(string code, string path = null) { var c = Evidence(code, path); return Diagnosis.Explain(c, @"C:\WWE", new[] { c }); }
Report RecentReport() => new Report { Crashes = new List<Crash> { new Crash { When = now.AddMinutes(-5), Code = "c0000005", Module = "WWE2K26_x64.exe", Offset = "1234", AppPath = @"C:\WWE\WWE2K26_x64.exe" } } };
void Assert(bool condition, string detail = "") { if (!condition) throw new Exception("Assertion failed. " + detail); }
void Contains(string value, string expected) { Assert(value != null && value.Contains(expected, StringComparison.OrdinalIgnoreCase), $"Expected '{expected}' in '{value}'."); }
void Test(string name, Action action) { try { action(); passed++; Console.WriteLine("PASS " + name); } catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex.Message); } }
