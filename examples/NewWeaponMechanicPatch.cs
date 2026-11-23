using GraveyardOrbit;
using HarmonyLib;
using UnityEngine;

namespace GraveFrameworkExamples
{
    // Proves a genuinely NEW weapon mechanic (not a reskin of existing behavior) is
    // possible via ModRuntime — the devs' own generic per-hit/per-kill dispatch system.
    // TileField fires OnTileHit/OnTileKilledBy on every real hit/kill; ModRuntime.HandleHit
    // looks up the hit part's equipped ModLoadout and, for each equipped mod id, invokes a
    // handler delegate registered by id (LaserMods/RotorMods/BreacherMods/PulseMods all
    // register their handlers once per sortie in MiningSortie.Start()). This postfix
    // registers one more: "shockwave" (defined as a new Mods-tree node via the already-
    // proven ModCatalog injection, content/mods/shockwave.json) — an AoE nova around every hit
    // using EnemyManager.DamageEnemiesInDisc, a public method official rotor mods already
    // use for the same kind of effect.
    [HarmonyPatch(typeof(MiningSortie), "Start")]
    internal static class NewWeaponMechanicPatch
    {
        private static void Postfix(ModRuntime ____modRuntime)
        {
            if (____modRuntime == null)
            {
                Plugin.Log.LogError("NewWeaponMechanic: ____modRuntime is null, cannot register.");
                return;
            }

            ____modRuntime.RegisterHit("shockwave", Shockwave);
            Plugin.Log.LogInfo("NewWeaponMechanic: registered 'shockwave' hit handler on ModRuntime.");
        }

        private static void Shockwave(ModRuntime rt, int idx, int dmgDealt, in HitInfo hit, ModRuntime.ModLoadout lo, int copies)
        {
            if (rt.Field == null)
            {
                return;
            }

            float radius = ModCatalog.Param("shockwave", "radius", copies, 2f);
            float dmgFrac = ModCatalog.Param("shockwave", "dmg_frac", copies, 0.5f);
            int dmg = Mathf.Max(1, Mathf.RoundToInt(dmgDealt * dmgFrac));

            Vector3 world = rt.Field.TileWorld(idx);
            rt.Enemies?.DamageEnemiesInDisc(world, radius, dmg);

            Plugin.Log.LogInfo($"NewWeaponMechanic: shockwave fired at tile idx={idx}, radius={radius}, dmg={dmg}, copies={copies}.");
        }
    }
}
