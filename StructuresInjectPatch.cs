using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using GraveyardOrbit;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace GraveyardOrbitModdingFramework
{
    // Injects wholly new, fully-registered contracts into the real player-visible Contracts
    // list (ModeSelectController reads StructureCatalog.I.Contracts, which resolves to
    // .campaign in the full non-demo game — see comment below). Constructs a StructureDef by
    // hand (ScriptableObject.CreateInstance, all its fields are public) with its cell data
    // baked into a runtime-constructed TextAsset. Supports Reload() so new/changed/removed
    // files in any mod's content/structures/ take effect via a hotkey, no game restart needed.
    //
    // Public (not internal) only so other plugins can call IsInjected(); everything else
    // stays private, and the tracker itself is still internal.
    [HarmonyPatch(typeof(StructureCatalog), "get_I")]
    public static class StructuresInjectPatch
    {
        private static readonly FieldInfo CacheField = AccessTools.Field(typeof(StructureCatalog), "_cache");
        private static readonly InjectedTracker Tracker = new InjectedTracker();

        // True if this structure id was injected from some mod's content/structures/*.json.
        public static bool IsInjected(string id) => id != null && Tracker.Contains(id);

        private static void Postfix(ref StructureCatalog __result)
        {
            if (__result == null)
            {
                return;
            }
            StructureCatalog cat = __result;
            Tracker.RunOnce(() => Apply(cat));
        }

        public static void Reload()
        {
            StructureCatalog cat = StructureCatalog.I;
            if (cat == null)
            {
                Plugin.Log.LogError("NewStructures: StructureCatalog.I is null, cannot reload.");
                return;
            }

            // Remove whatever we previously injected before re-scanning, so editing or
            // deleting a file doesn't leave stale/duplicate entries behind.
            Tracker.Reload(
                ids =>
                {
                    if (cat.campaign != null)
                    {
                        cat.campaign = cat.campaign.Where(d => d == null || !ids.Contains(d.id)).ToArray();
                    }
                    var cache = (Dictionary<string, StructureDef>)CacheField.GetValue(null);
                    foreach (string id in ids)
                    {
                        cache.Remove(id);
                    }
                },
                () => Apply(cat));
        }

        private static void Apply(StructureCatalog cat)
        {
            ContentInjector.ScanFolder<StructureDef>(
                "NewStructures", ModDiscovery.Current, "structures",
                id => cat.campaign != null && cat.campaign.Any(d => d != null && d.id == id),
                (o, file) => BuildFromJson(o, Path.GetDirectoryName(file)),
                (def, id) =>
                {
                    // StructureCatalog.Contracts (what ModeSelectController's Overtime/"Star
                    // Contracts" screen actually reads) falls back to .ladder only when
                    // CampaignActive is false — and in the full (non-demo) game, .campaign
                    // always has entries, so CampaignActive is always true and Contracts
                    // always resolves to .campaign in practice. Append there, not .ladder.
                    int oldLen = cat.campaign?.Length ?? 0;
                    Array.Resize(ref cat.campaign, oldLen + 1);
                    cat.campaign[oldLen] = def;
                    Tracker.Add(id);
                    Plugin.Log.LogInfo($"NewStructures: injected '{id}' into StructureCatalog.campaign (now {cat.campaign.Length} entries).");
                },
                skipSuffix: "_cells.json");
        }

        private static StructureDef BuildFromJson(JObject o, string dir)
        {
            string id = (string)o["id"];

            string cellsFile = (string)o["cells_file"];
            string cellsJson = null;
            int cellCount = 0;
            if (!string.IsNullOrEmpty(cellsFile))
            {
                string cellsPath = Path.Combine(dir, cellsFile);
                if (File.Exists(cellsPath))
                {
                    cellsJson = File.ReadAllText(cellsPath);
                    JObject cellsObj = JObject.Parse(cellsJson);
                    cellCount = (int?)cellsObj["count"] ?? 0;
                }
            }

            StructureDef def = ScriptableObject.CreateInstance<StructureDef>();
            def.id = id;
            def.displayName = (string)o["display_name"] ?? id;
            def.description = (string)o["description"] ?? "";
            def.cellCount = cellCount;
            def.sealedLayers = (bool?)o["sealed_layers"] ?? true;
            def.startingMoney = (int?)o["starting_money"] ?? 0;
            def.glyphCount = (int?)o["glyph_count"] ?? 0;
            def.greenCount = (int?)o["green_count"] ?? -1;

            if (cellsJson != null)
            {
                def.cells = new TextAsset(cellsJson) { name = id + "_cells" };
            }

            if (o["bands"] is JArray bandsArr)
            {
                def.bands = new BandDef[bandsArr.Count];
                for (int i = 0; i < bandsArr.Count; i++)
                {
                    JObject b = (JObject)bandsArr[i];
                    def.bands[i] = new BandDef
                    {
                        layer = (int?)b["layer"] ?? 1,
                        outerFrac = (float?)b["outer_frac"] ?? 1f,
                        hpSealed = (int?)b["hp_sealed"] ?? 75
                    };
                }
            }

            return def;
        }
    }
}
