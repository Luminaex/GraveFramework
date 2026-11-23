using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace GraveyardOrbitModdingFramework
{
    internal enum LoadOutcome
    {
        Injected,
        Skipped,  // harmless: id already in the live game catalog
        Rejected  // a problem: no id, cross-mod collision, failed validation, load error
    }

    // Collects every content file's outcome for one load pass (boot, or one Ctrl+F6
    // hot-reload) so Plugin can print a single per-mod summary to LogOutput.log.
    internal static class LoadReport
    {
        private sealed class Entry
        {
            public string ModId;
            public string ContentType;
            public LoadOutcome Outcome;
            public string Id;
            public string Reason;
        }

        private static readonly List<Entry> Entries = new List<Entry>();

        public static void Reset() => Entries.Clear();

        public static void Record(string modId, string contentType, LoadOutcome outcome, string id, string reason = null)
        {
            Entries.Add(new Entry { ModId = modId, ContentType = contentType, Outcome = outcome, Id = id, Reason = reason });
        }

        public static string BuildReportText()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== Content load report ===");

            IReadOnlyList<DiscoveredMod> mods = ModDiscovery.Current;
            sb.AppendLine($"Discovered mods ({mods.Count}):");
            foreach (DiscoveredMod m in mods)
            {
                string deps = m.Dependencies != null && m.Dependencies.Length > 0 ? string.Join(", ", m.Dependencies) : null;
                sb.AppendLine($"  {Dash(m.Id)}  name={Dash(m.Name)}  version={Dash(m.Version)}  dependencies={Dash(deps)}  ({m.RootDir})");
            }

            if (Entries.Count == 0)
            {
                sb.Append("No content files processed yet.");
                return sb.ToString();
            }

            // Known types first in their usual order, then anything else alphabetically.
            var types = ModDiscovery.ContentTypes.Where(t => Entries.Any(e => e.ContentType == t))
                .Concat(Entries.Select(e => e.ContentType).Distinct()
                    .Where(t => !ModDiscovery.ContentTypes.Contains(t))
                    .OrderBy(t => t, StringComparer.Ordinal));

            sb.AppendLine("Content:");
            foreach (string type in types)
            {
                foreach (var byMod in Entries.Where(e => e.ContentType == type)
                             .GroupBy(e => e.ModId)
                             .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
                {
                    sb.AppendLine($"  {type}: {byMod.Key} injected={byMod.Count(e => e.Outcome == LoadOutcome.Injected)} skipped={byMod.Count(e => e.Outcome == LoadOutcome.Skipped)} rejected={byMod.Count(e => e.Outcome == LoadOutcome.Rejected)}");
                }
            }

            var problems = Entries.Where(e => e.Outcome != LoadOutcome.Injected).ToList();
            if (problems.Count > 0)
            {
                sb.AppendLine("Not loaded:");
                foreach (Entry e in problems)
                {
                    sb.AppendLine($"  {e.ContentType}: {e.ModId} '{e.Id}' {e.Outcome.ToString().ToLowerInvariant()}: {Dash(e.Reason)}");
                }
            }

            return sb.ToString().TrimEnd();
        }

        private static string Dash(string s) => string.IsNullOrWhiteSpace(s) ? "-" : s;
    }
}
