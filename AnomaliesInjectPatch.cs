using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GraveyardOrbit;
using HarmonyLib;
using Newtonsoft.Json.Linq;

namespace GraveFramework
{
    // Anomalies ("artifacts") are the one system where new content needs an extra patch
    // beyond data injection: effect dispatch in MiningSortie.FireAnomaly(string id) is a
    // hardcoded switch(id) — a brand-new id would hit no case and silently no-op. This
    // proves the workaround: alias a new id to an existing effect's public entry point
    // (GameState.TriggerWarCry) via a Harmony prefix that intercepts before the switch.
    // Supports Reload() for hotkey-triggered re-scan without a game restart.
    [HarmonyPatch(typeof(AnomalyCatalog), "EnsureLoaded")]
    internal static class AnomaliesInjectPatch
    {
        private static readonly FieldInfo AllField = AccessTools.Field(typeof(AnomalyCatalog), "_all");
        private static readonly FieldInfo ByIdField = AccessTools.Field(typeof(AnomalyCatalog), "_byId");

        // Shared with FireAnomalyAliasPatch below — any id we've injected is automatically
        // aliased to the generic effect handler, no separate list to keep in sync.
        internal static readonly InjectedTracker Tracker = new InjectedTracker();

        private static void Postfix() => Tracker.RunOnce(Apply);

        public static void Reload() => Tracker.Reload(
            ids =>
            {
                var all = (List<AnomalyDef>)AllField.GetValue(null);
                var byId = (Dictionary<string, AnomalyDef>)ByIdField.GetValue(null);
                all.RemoveAll(a => ids.Contains(a.id));
                foreach (string id in ids)
                {
                    byId.Remove(id);
                    AnomalyCatalog.Implemented.Remove(id);
                }
            },
            Apply);

        private static void Apply()
        {
            var all = (List<AnomalyDef>)AllField.GetValue(null);
            var byId = (Dictionary<string, AnomalyDef>)ByIdField.GetValue(null);

            ContentInjector.ScanFolder<AnomalyDef>(
                "NewAnomalies", ModDiscovery.Current, "anomalies",
                byId.ContainsKey,
                BuildFromJson,
                (def, id) =>
                {
                    all.Add(def);
                    byId[id] = def;
                    Tracker.Add(id);
                    // AnomalyCatalog.Implemented is a public static readonly HashSet<string> —
                    // mutable contents, just not reassignable. Add our id so anything that
                    // checks "is this anomaly real" (not every effect-dispatch call site
                    // does, but some UI/offer logic might) treats it as legitimate.
                    AnomalyCatalog.Implemented.Add(id);
                    Plugin.Log.LogInfo($"NewAnomalies: injected '{id}' (band={def.band}, charge={def.chargeSecs}s, effect={def.effectSecs}s).");
                });
        }

        private static AnomalyDef BuildFromJson(JObject o, string file)
        {
            return new AnomalyDef
            {
                id = (string)o["id"],
                name = (string)o["name"],
                band = (string)o["band"],
                blurb = (string)o["blurb"],
                chargeSecs = (float?)o["chargeSecs"] ?? 75f,
                effectSecs = (float?)o["effectSecs"] ?? 0f,
                p = o["params"] as JObject
            };
        }
    }

    [HarmonyPatch(typeof(MiningSortie), "FireAnomaly")]
    internal static class FireAnomalyAliasPatch
    {
        // Every new anomaly id we inject gets aliased to GameState.TriggerWarCry (a public,
        // easily-callable existing effect) so it does something real and observable instead
        // of silently hitting no case in the official switch(id). A real mod would map each
        // new id to whichever existing effect family fits it best (or write genuinely new
        // effect code called from here directly).
        private static bool Prefix(string id, ref bool __result)
        {
            if (!AnomaliesInjectPatch.Tracker.Contains(id))
            {
                return true; // not ours — let the original switch(id) handle it
            }

            GameState gs = GameState.I;
            AnomalyDef def = AnomalyCatalog.Get(id);
            if (gs == null || def == null)
            {
                __result = false;
                return false;
            }

            gs.TriggerWarCry(def.effectSecs, def.P("damage", 5f), def.P("rate", 2f));
            Plugin.Log.LogInfo($"NewAnomalies: fired custom anomaly '{id}' via TriggerWarCry alias (effect={def.effectSecs}s, damage={def.P("damage", 5f)}, rate={def.P("rate", 2f)}).");
            __result = true;
            return false; // skip the original method body entirely
        }
    }
}
