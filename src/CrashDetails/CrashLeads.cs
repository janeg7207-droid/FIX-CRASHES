using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Gorilla.Manager.Engine;

namespace Gorilla.CrashDetails
{
    public sealed class FileCrashLead
    {
        public int Priority;
        public string Title;
        public string Evidence;
        public string Next;
    }

    public static class CrashLeads
    {
        public static void Append(Report report, IEnumerable<FileCrashLead> fileLeads)
        {
            report.Findings.RemoveAll(f => f != null && f.Id == "gorilla-cak-crash-leads");
            var leads = fileLeads.OrderByDescending(l => l.Priority).Take(8).ToList();
            var text = new StringBuilder();
            if (leads.Count == 0)
                text.AppendLine("No file-specific crash cause has been established. Identical copies or a shared path alone do not identify a bad mod. Read the Windows crash explanation and use the existing mod search to reproduce the same failure.");
            else
            {
                text.AppendLine("These are ranked investigation leads. Confirmed file defects are distinguished from their unconfirmed connection to the game crash.");
                for (int i = 0; i < leads.Count; i++)
                    text.AppendLine("\n" + (i + 1) + ". " + leads[i].Title + "\nEvidence: " + leads[i].Evidence + "\nNext: " + leads[i].Next);
            }
            foreach (Suspect suspect in (report.Suspects ?? new List<Suspect>()).Where(s => s != null).Take(3))
                text.AppendLine("\nExisting manager suspect: " + suspect.Title + ". " + string.Join("; ", suspect.Why ?? new List<string>()) + ". Timing/ranking is a lead, not proof of causation.");
            text.Append("\nTo isolate a cause, repeat the same scene with the named mod off, then restored, using the existing backup and mod-search controls. A crash dump or the loader's detailed logs may still be needed to identify a specific internal asset. No automatic deletion or disabling is performed.");
            report.Findings.Add(new Finding { Id = "gorilla-cak-crash-leads", Title = leads.Count > 0 ? "Crash investigation: " + leads[0].Title : "Crash cause: no file-specific cause established", Text = text.ToString() });
        }

        public static void AppendSearchResult(Report report, FinderState state)
        {
            report.Findings.RemoveAll(f => f != null && f.Id == "gorilla-mod-search-evidence");
            if (state == null || !state.Active || state.Result == null) return;
            bool none = state.Result == "none";
            report.Findings.Add(new Finding
            {
                Id = "gorilla-mod-search-evidence",
                Title = none ? "Mod search did not isolate one mod" : "Mod search isolated: " + state.Result,
                Text = none
                    ? "The completed search did not isolate one mod. An interaction, baked content, saved data, drivers or the game itself still need investigation. No single file is confirmed responsible."
                    : "The manager's recorded search result names " + state.Result + " after " + state.Round + " test round(s). Disabling that archive was reported to remove the problem. This is reproduction evidence for the archive or an interaction involving it; it does not identify a particular internal file or prove its code is defective. Repeat with the same scene and the archive restored to confirm the relationship. A result from an earlier setup may no longer apply."
            });
        }
    }
}
