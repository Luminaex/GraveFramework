using System;
using System.Linq;
using GraveyardOrbit;
using HarmonyLib;
using Newtonsoft.Json.Linq;

namespace GraveyardOrbitModdingFramework
{
    // The simplest injection in the whole project: QuotaCatalog.All is a plain public
    // static List<QuotaDef> (not gated by EnsureLoaded, not TextAsset-backed) — no Harmony
    // method patch and no reflection needed at all, just .Add(). QuotaState.Draw() reads
    // QuotaCatalog.All directly and buckets quotas by QuotaDef's computed properties
    // (IsGeneral/IsMaterial/IsFamily/IsCross/IsPurchase, all derived from `goal`), so a new
    // quota reusing an existing QuotaGoal lands in the same draw pool as official ones.
    [HarmonyPatch(typeof(GameConfig), "Load")]
    internal static class QuotasInjectPatch
    {
        private static readonly InjectedTracker Tracker = new InjectedTracker();

        private static void Postfix() => Tracker.RunOnce(Apply);

        public static void Reload() => Tracker.Reload(
            ids => QuotaCatalog.All.RemoveAll(q => ids.Contains(q.id)),
            Apply);

        private static void Apply()
        {
            ContentInjector.ScanFolder<QuotaDef>(
                "NewQuotas", ModDiscovery.Current, "quotas",
                id => QuotaCatalog.All.Any(q => q.id == id),
                BuildFromJson,
                (def, id) =>
                {
                    QuotaCatalog.All.Add(def);
                    Tracker.Add(id);
                    Plugin.Log.LogInfo($"NewQuotas: injected '{id}' (goal={def.goal}, target={def.target}, reward={def.reward}).");
                });
        }

        private static QuotaDef BuildFromJson(JObject o, string file)
        {
            string id = (string)o["id"];
            string goalStr = (string)o["goal"];
            if (!Enum.TryParse(goalStr, out QuotaGoal goal))
            {
                Plugin.Log.LogError($"NewQuotas: '{id}' has unknown goal '{goalStr}', skipping.");
                return null;
            }

            return new QuotaDef
            {
                id = id,
                goal = goal,
                target = (int?)o["target"] ?? 0,
                reward = (int?)o["reward"] ?? 0,
                text = (string)o["text"] ?? id,
                materialId = (string)o["material_id"],
                family = (string)o["family"],
                rewardMeta = (string)o["reward_meta"],
                rewardMetaCount = (int?)o["reward_meta_count"] ?? 0,
                rewardRoll = (bool?)o["reward_roll"] ?? false
            };
        }
    }
}
