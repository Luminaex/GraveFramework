using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;

namespace GraveFramework
{
    // Shared plumbing for every "scan each mod's content/<type> folder of *.json files and
    // inject defs into a live catalog" patch (Structures, Mods, Boosters, Anomalies, Quotas
    // as of writing — see SURVEY.md). Each injector still owns its own JSON schema (via
    // `build`) and its own catalog-specific add/remove logic (List vs array vs Dictionary vs
    // extra side collections like AnomalyCatalog.Implemented) — this only collapses the part
    // that was being hand-copied nearly verbatim five times: folder resolution, id-presence
    // and duplicate checking, cross-mod collision handling, per-file error isolation,
    // consistent logging, and LoadReport bookkeeping.
    internal static class ContentInjector
    {
        // For every discovered mod (in the order given — ModDiscovery sorts by id), scans
        // <mod.RootDir>/content/<folderName> for *.json files and hands each one's parsed
        // JObject (plus its own id, already validated non-empty and not a duplicate) to
        // `build`. `build` returning null means it already logged its own reason (bad enum,
        // failed game-side validation, etc.) and the file is skipped silently from here.
        //
        // Cross-mod collisions: the first mod to successfully inject an id owns it for this
        // call; a later mod's file declaring the same id is rejected (logged naming both
        // mods) before `alreadyExists`/`build`/`onAdd` ever run.
        //
        // A bad/corrupt file only skips that one file; the rest of the folder (and every
        // other mod) still loads.
        public static void ScanFolder<TDef>(
            string tag,
            IEnumerable<DiscoveredMod> mods,
            string folderName,
            Func<string, bool> alreadyExists,
            Func<JObject, string, TDef> build,
            Action<TDef, string> onAdd,
            string skipSuffix = null) where TDef : class
        {
            var idOwner = new Dictionary<string, string>(); // content id -> mod id that injected it

            foreach (DiscoveredMod mod in mods)
            {
                string dir = Path.Combine(mod.RootDir, "content", folderName);
                if (!Directory.Exists(dir))
                {
                    continue;
                }

                foreach (string file in Directory.GetFiles(dir, "*.json"))
                {
                    if (skipSuffix != null && file.EndsWith(skipSuffix, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string id = null;
                    try
                    {
                        JObject o = JObject.Parse(File.ReadAllText(file));
                        id = (string)o["id"];
                        if (string.IsNullOrEmpty(id))
                        {
                            Plugin.Log.LogError($"{tag}: '{file}' has no 'id', skipping.");
                            LoadReport.Record(mod.Id, folderName, LoadOutcome.Rejected, Path.GetFileName(file), "no 'id'");
                            continue;
                        }
                        // Same-mod duplicates fall through to alreadyExists below, as before.
                        if (idOwner.TryGetValue(id, out string owner) && owner != mod.Id)
                        {
                            string reason = $"id collision: mod '{mod.Id}' declares '{id}', already claimed by mod '{owner}'";
                            Plugin.Log.LogError($"{tag}: {reason}, skipping ({file}).");
                            LoadReport.Record(mod.Id, folderName, LoadOutcome.Rejected, id, reason);
                            continue;
                        }
                        if (alreadyExists(id))
                        {
                            Plugin.Log.LogError($"{tag}: '{id}' already exists, skipping ({file}).");
                            LoadReport.Record(mod.Id, folderName, LoadOutcome.Skipped, id, "already exists in the game catalog");
                            continue;
                        }

                        TDef def = build(o, file);
                        if (def == null)
                        {
                            LoadReport.Record(mod.Id, folderName, LoadOutcome.Rejected, id, "failed validation (see log above)");
                            continue;
                        }

                        onAdd(def, id);
                        idOwner[id] = mod.Id;
                        LoadReport.Record(mod.Id, folderName, LoadOutcome.Injected, id);
                    }
                    catch (Exception e)
                    {
                        Plugin.Log.LogError($"{tag}: failed to load '{file}': {e}");
                        LoadReport.Record(mod.Id, folderName, LoadOutcome.Rejected, id ?? Path.GetFileName(file), "load error: " + e.Message);
                    }
                }
            }
        }
    }

    // Replaces the "HashSet<string> InjectedIds + bool _done" pair every injector
    // hand-rolled, plus the RunOnce/Reload shape built around them.
    internal sealed class InjectedTracker
    {
        private readonly HashSet<string> _ids = new HashSet<string>();
        private bool _done;

        public void Add(string id) => _ids.Add(id);

        public bool Contains(string id) => _ids.Contains(id);

        // The Postfix-side "only inject once" guard — runs `apply` on the first call only.
        public void RunOnce(Action apply)
        {
            if (_done)
            {
                return;
            }
            _done = true;
            apply();
        }

        // The standard Reload() shape every injector repeated: let the caller strip out
        // everything previously tracked (from whatever collections/side-effects it owns),
        // clear tracking, then re-scan.
        public void Reload(Action<IReadOnlyCollection<string>> remove, Action apply)
        {
            if (_ids.Count > 0)
            {
                remove(_ids);
                _ids.Clear();
            }
            apply();
        }
    }
}
