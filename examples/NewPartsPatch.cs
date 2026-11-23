using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using GraveyardOrbit;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace GraveFrameworkExamples
{
    // Injects community-authored new ship parts into the live GameData right after
    // GameConfig.Load() hydrates from the baked GameData.asset. A "new part" needs three
    // things, none of which require touching compiled behavior code:
    //   1. A GdPartDef entry in GameData.Parts (base stats: cost/hp/mass/power/stats dict)
    //   2. A GdPartUpgradeDef entry in GameData.Upgrades (family/role/parent/lanes), built
    //      via the game's own private GameData.ParseUpgrade so it stays schema-compatible
    //   3. A PartBridge.MarkBaseByModel entry pointing at an EXISTING PartKind's id, so
    //      PartBridge.TryBehaviourKind resolves our new id to real, working weapon/part
    //      behavior (PartKind itself is a compiled C# enum — we reuse an existing behavior
    //      rather than invent a new one). PartRenderer's private FileNames dict is also
    //      seeded so the part renders with an existing model instead of falling back to a
    //      tinted placeholder cube.
    [HarmonyPatch(typeof(GameConfig), "Load")]
    internal static class NewPartsInjectPatch
    {
        private static readonly MethodInfo ParseUpgradeMethod =
            AccessTools.Method(typeof(GameData), "ParseUpgrade");
        private static bool _done;

        private static void Postfix()
        {
            if (_done)
            {
                return;
            }
            _done = true;
            Apply();
        }

        // Dict-keyed assignment (GameData.Parts[id]=..., GameData.Upgrades[id]=...) is
        // naturally idempotent — re-running just overwrites with the current file content,
        // no separate removal step needed like the list-based catalogs.
        public static void Reload()
        {
            Apply();
        }

        private static void Apply()
        {
            try
            {
                string pluginDir = Path.GetDirectoryName(typeof(NewPartsInjectPatch).Assembly.Location);
                string newPartsDir = Path.Combine(pluginDir, "new_parts");
                if (!Directory.Exists(newPartsDir))
                {
                    return;
                }

                foreach (string file in Directory.GetFiles(newPartsDir, "*.json"))
                {
                    InjectOne(file);
                }

                AccretionPartDef.Catalog.Invalidate();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"NewParts injection failed: {e}");
            }
        }

        private static void InjectOne(string path)
        {
            JObject root = JObject.Parse(File.ReadAllText(path));
            string id = (string)root["id"];
            if (string.IsNullOrEmpty(id))
            {
                Plugin.Log.LogError($"NewParts: '{path}' has no 'id', skipping.");
                return;
            }

            string visualBase = (string)root["visual_base"];

            // --- GdPartDef (base stats) ---
            JObject partObj = root["part"] as JObject;
            if (partObj != null)
            {
                GdPartDef def = new GdPartDef
                {
                    id = id,
                    name = (string)partObj["name"] ?? id,
                    abbr = (string)partObj["abbr"],
                    desc = (string)partObj["desc"],
                    color = GameData.HexToColor((string)partObj["color"], Color.white),
                    cost = (int?)partObj["cost"] ?? 0,
                    powerCost = (int?)partObj["power_cost"] ?? 0,
                    powerOutput = (int?)partObj["power_output"] ?? 0,
                    mass = (int?)partObj["mass"] ?? 1,
                    hp = (int?)partObj["hp"] ?? 1,
                    energy = (int?)partObj["energy"] ?? 0
                };
                if (partObj["stats"] is JObject statsObj)
                {
                    foreach (JProperty prop in statsObj.Properties())
                    {
                        def.stats[prop.Name] = (float)prop.Value;
                    }
                }
                GameData.Parts[id] = def;
            }

            // --- GdPartUpgradeDef (family/role/parent/lanes) via the game's own parser ---
            JObject upgradeObj = root["upgrade"] as JObject;
            if (upgradeObj != null)
            {
                object parsed = ParseUpgradeMethod.Invoke(null, new object[] { id, upgradeObj });
                GameData.Upgrades[id] = (GdPartUpgradeDef)parsed;
            }

            // --- Wire the new id to an existing PartKind's behavior ---
            if (!string.IsNullOrEmpty(visualBase))
            {
                PartBridge.MarkBaseByModel[id] = visualBase;

                // PartRenderer.FileNames is private; reuse the base model's visual so the
                // part doesn't fall back to a plain tinted cube.
                FieldInfo fileNamesField = AccessTools.Field(typeof(PartRenderer), "FileNames");
                var fileNames = (Dictionary<string, string>)fileNamesField.GetValue(null);
                if (fileNames.TryGetValue(visualBase, out string baseFile))
                {
                    fileNames[id] = baseFile;
                }
            }

            Plugin.Log.LogInfo($"NewParts: injected '{id}' (visual_base='{visualBase}').");
        }
    }
}
