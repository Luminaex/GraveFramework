using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GraveyardOrbit;
using HarmonyLib;
using Newtonsoft.Json.Linq;

namespace GraveFramework
{
    // Injects community-authored nodes into the "Mods" research tree right after
    // ModCatalog.EnsureLoaded() parses the official Resources/progression/mods TextAsset.
    // Reuses the game's own private Validate/ValidateGraph so a bad node (duplicate grid
    // cell, unknown parent, bad kind, etc.) is rejected exactly like the official loader
    // would reject it, instead of silently corrupting the tree. Supports Reload() for
    // hotkey-triggered re-scan without a game restart.
    [HarmonyPatch(typeof(ModCatalog), "EnsureLoaded")]
    internal static class ModsInjectPatch
    {
        private static readonly FieldInfo AllField = AccessTools.Field(typeof(ModCatalog), "_all");
        private static readonly FieldInfo ByIdField = AccessTools.Field(typeof(ModCatalog), "_byId");
        private static readonly MethodInfo ValidateMethod = AccessTools.Method(typeof(ModCatalog), "Validate");
        private static readonly MethodInfo ValidateGraphMethod = AccessTools.Method(typeof(ModCatalog), "ValidateGraph");
        private static readonly InjectedTracker Tracker = new InjectedTracker();

        private static void Postfix() => Tracker.RunOnce(Apply);

        public static void Reload() => Tracker.Reload(
            ids =>
            {
                var all = (List<ModCatalog.ModDef>)AllField.GetValue(null);
                var byId = (Dictionary<string, ModCatalog.ModDef>)ByIdField.GetValue(null);
                all.RemoveAll(d => ids.Contains(d.id));
                foreach (string id in ids)
                {
                    byId.Remove(id);
                }
            },
            Apply);

        private static void Apply()
        {
            var all = (List<ModCatalog.ModDef>)AllField.GetValue(null);
            var byId = (Dictionary<string, ModCatalog.ModDef>)ByIdField.GetValue(null);
            bool added = false;

            ContentInjector.ScanFolder<ModCatalog.ModDef>(
                "NewMods", ModDiscovery.Current, "mods",
                byId.ContainsKey,
                BuildFromJson,
                (def, id) =>
                {
                    all.Add(def);
                    byId[id] = def;
                    Tracker.Add(id);
                    added = true;
                    Plugin.Log.LogInfo($"NewMods: injected '{id}' (arm={def.arm}, kind={def.kind}, row={def.row}, col={def.col}).");
                });

            // ValidateGraph checks cross-node invariants (parent chains etc), so it only
            // needs to run once after the whole folder's been scanned, not per file.
            if (added)
            {
                ValidateGraphMethod.Invoke(null, null);
            }
        }

        private static ModCatalog.ModDef BuildFromJson(JObject o, string file)
        {
            ModCatalog.ModDef def = new ModCatalog.ModDef
            {
                id = (string)o["id"],
                name = (string)o["name"],
                arm = (string)o["arm"],
                kind = (string)o["kind"],
                legacy = (string)o["legacy"],
                blurb = (string)o["blurb"] ?? "",
                perCopy = (string)o["perCopy"] ?? "",
                glyph = (string)o["glyph"],
                col = (int?)o["col"] ?? -1,
                row = (int?)o["row"] ?? -1
            };
            def.tier = ModCatalog.TierOfRow(def.row);

            if (o["fits"] is JArray fitsArr)
            {
                def.fits = new string[fitsArr.Count];
                for (int i = 0; i < fitsArr.Count; i++)
                {
                    def.fits[i] = (string)fitsArr[i];
                }
            }
            if (o["parents"] is JArray parentsArr)
            {
                def.parents = new string[parentsArr.Count];
                for (int i = 0; i < parentsArr.Count; i++)
                {
                    def.parents[i] = (string)parentsArr[i];
                }
            }
            if (o["price"] is JObject priceObj)
            {
                foreach (JProperty prop in priceObj.Properties())
                {
                    def.price[prop.Name] = (int)prop.Value;
                }
            }
            if (o["params"] is JObject paramsObj)
            {
                foreach (JProperty prop in paramsObj.Properties())
                {
                    if (prop.Value.Type == JTokenType.String)
                    {
                        def.sparam[prop.Name] = (string)prop.Value;
                    }
                    else if (prop.Value.Type == JTokenType.Integer || prop.Value.Type == JTokenType.Float)
                    {
                        def.param[prop.Name] = (float)prop.Value;
                    }
                }
            }

            string validationError = (string)ValidateMethod.Invoke(null, new object[] { def });
            if (validationError != null)
            {
                Plugin.Log.LogError($"NewMods: '{def.id}' invalid ({validationError}), skipping ({file}).");
                return null;
            }

            return def;
        }
    }
}
