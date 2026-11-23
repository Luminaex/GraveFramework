using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GraveyardOrbit;
using HarmonyLib;
using Newtonsoft.Json.Linq;

namespace GraveFramework
{
    // Injects community-authored Boosters (mid-run stat buffs) right after
    // BoosterCatalog.EnsureLoaded() parses the official Resources/boost_cards TextAsset.
    // Supports Reload() for hotkey-triggered re-scan without a game restart.
    [HarmonyPatch(typeof(BoosterCatalog), "EnsureLoaded")]
    internal static class BoostersInjectPatch
    {
        private static readonly FieldInfo AllField = AccessTools.Field(typeof(BoosterCatalog), "_all");
        private static readonly InjectedTracker Tracker = new InjectedTracker();

        private static void Postfix() => Tracker.RunOnce(Apply);

        public static void Reload() => Tracker.Reload(
            ids =>
            {
                var all = (List<BoosterDef>)AllField.GetValue(null);
                all.RemoveAll(b => ids.Contains(b.id));
            },
            Apply);

        private static void Apply()
        {
            var all = (List<BoosterDef>)AllField.GetValue(null);

            ContentInjector.ScanFolder<BoosterDef>(
                "NewBoosters", ModDiscovery.Current, "boosters",
                id => all.Any(b => b.id == id),
                BuildFromJson,
                (def, id) =>
                {
                    all.Add(def);
                    Tracker.Add(id);
                    Plugin.Log.LogInfo($"NewBoosters: injected '{id}' (rarity={def.rarity}, stats={string.Join(",", def.stats.Keys)}).");
                });
        }

        private static BoosterDef BuildFromJson(JObject o, string file)
        {
            string rarityStr = (string)o["rarity"];
            BoosterRarity rarity = rarityStr == "gold" ? BoosterRarity.Gold
                : rarityStr == "rare" ? BoosterRarity.Rare
                : BoosterRarity.Common;

            BoosterDef def = new BoosterDef
            {
                id = (string)o["id"],
                name = (string)o["name"],
                group = (string)o["group"],
                rarity = rarity
            };
            if (o["stats"] is JObject statsObj)
            {
                foreach (JProperty prop in statsObj.Properties())
                {
                    def.stats[prop.Name] = (float)prop.Value;
                }
            }

            return def;
        }
    }
}
