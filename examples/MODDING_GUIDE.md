# Modding Guide

A consolidated reference for every proven way to add real content to Graveyard Orbit via
GraveFramework, plus a couple of techniques that don't need the framework at all. Each section
names the exact file that demonstrates it in this `examples/` project — read that file's own
comments for the precise schema/fields; this guide is the map, not the territory.

Everything here has been **built and verified running live in-game**, except where a section
is explicitly marked otherwise. If you're extending one of these, copy the matching file as your
starting point rather than writing a patch from scratch.

## Zero-code techniques (just drop JSON files, no DLL required)

GraveFramework auto-discovers any folder under `BepInEx/plugins/` containing a `manifest.json`
or a `content/<type>/` folder. Each type below is a plain folder of `*.json` files matching the
game's own schema for that catalog — see the matching `*InjectPatch.cs` in the main
`GraveFramework` repo (not this `examples/` project) for the exact fields each type expects.

| Content type | Folder | Demo file in this project |
|---|---|---|
| Research-tree "Mods" node | `content/mods/` | `content/mods/l_prism_shot.json`, `shockwave.json` |
| Booster card | `content/boosters/` | `content/boosters/mod_test_booster.json` |
| Anomaly ("artifact") | `content/anomalies/` | `content/anomalies/mod_test_anomaly.json` |
| Quota (work order) | `content/quotas/` | `content/quotas/destroy_6000.json` |
| Structure/contract | `content/structures/` | not demoed here — GraveBuilder's own "Export Structure" feature produces these, see that repo instead |

Cross-mod id collisions are detected and logged automatically (naming both mods) — you don't
need to coordinate ids with anyone, just don't expect a silent overwrite if you collide.

## Code-based techniques (the content folders don't cover these)

### New campaign chapter — `NewCampaignChapterPatch.cs`
Chapter numbering is array position, not id-based: append to `StructureCatalog.chapters` the
same way `StructuresInjectPatch` appends to `.campaign`, then append a matching story beat at
the same index. Three separate hardcoded `5`s (chapter count, in three different classes) had
to be patched too — a recurring shape in this game, see "General patterns" below.

### New ship part / weapon variant — `NewPartsPatch.cs`
Harmony-postfixes `GameConfig.Load()` to inject a new `GdPartDef`/`GdPartUpgradeDef` pair via
the game's own private `GameData.ParseUpgrade` (by reflection), aliases it to an existing base
weapon's behavior via `PartBridge.MarkBaseByModel`, and seeds `PartRenderer`'s model-lookup so
it doesn't fall back to a tinted placeholder cube. **Known ceiling:** a wholly new weapon
*family* won't reach the normal shop/builder UI — `PartTiers.ShopShows()` hard-filters to the 5
base arm model ids. A **variant** that crosses the existing MK-tier ladder (this demo's
"Longshot" laser) does reach players; see the `role: "variant"`/`unlock_gate`/`mark` fields in
`new_parts_source/laser_sniper.json`.

### New weapon hit-mechanic — `NewWeaponMechanicPatch.cs`
`ModRuntime` is the game's own generic per-hit/per-kill dispatch system: `TileField` fires
`OnTileHit`/`OnTileKilledBy`, `ModRuntime.HandleHit` looks up the hit part's equipped mod ids
and invokes a registered delegate. A Harmony postfix on `MiningSortie.Start()` (grabbing the
private `_modRuntime` field via `____modRuntime` — **four** underscores, since the real field
name itself starts with one and Harmony's `___` convention strips exactly three) registers a
new handler by id, which gets public access to `EnemyManager`'s existing AoE/stun/vortex/sweep
helpers. Pair with a Mods-tree node (zero-code technique above) so it's equippable.

### Mod-supported localisation — `LocalisationInjectPatch.cs`
The most reflection-heavy technique here. `Loc.Languages` is extended via a plain field write
(readonly only blocks reassignment from normal code, not `FieldInfo.SetValue`), but
`Loc.Load(Language)`'s private nested `Entry` struct and private `Dictionary<string, Entry>
_table` field can't be touched with any compile-time-typed code at all — solved via
`AccessTools.Inner` + `Activator.CreateInstance` + `Type.MakeGenericType` to build a correctly-
typed dictionary purely through reflection, then a Harmony prefix on `Loc.Load` that returns
`false` (skips the original) only when a matching language file exists, falling through to the
normal path for every other language untouched. **Not wired into hot-reload** — a new/changed
language needs a full game restart, since this touches state set up once at boot.

### A mod documenting itself in the Handbook — `HandbookInjectPatch.cs`
**⚠ Built but not yet verified live in-game — treat as unconfirmed, unlike everything else in
this guide.** `HandbookBook.Entry`/`Section`/`Block`/`Card` are public nested classes, but the
loaded instance (private static `Book _book`) isn't exposed — same shape as every catalog
above: reflect into the private field once `HandbookBook.Reload()` has run, and append to its
`sections`/`entries` lists. Guard against re-adding the same ids, since `Reload()` re-runs on
every language change.

## Building your own menu UI (not covered here)

Adding a button/panel to a screen (main menu, pause menu, etc.) instead of injecting data into
a catalog is a different kind of technique — see
[`UI_GUIDE.md`](../UI_GUIDE.md) in the main `GraveFramework` project, worked through using
`ModsMenuPatch.cs` (the main menu's "MODS (n)" button) as the example.

## One-liner techniques (no Harmony patch needed at all)

- **`GraveyardOrbit.BuildingUnlocks.AllOpen`** is a `public static bool` (not even readonly) —
  set it `true` from anywhere (a mod's `Awake()`, a debug console) to unlock every home-base
  station instantly. No reflection, no patch. Good for a "QoL/testing" toggle.
- **`QuotaCatalog.All`** is a plain `public static List<QuotaDef>` — `QuotaCatalog.All.Add(new
  QuotaDef{...})` with zero reflection reuses any of the 10 existing `QuotaGoal` types (a wholly
  new goal type needs a C# tracking hook, not covered here). The zero-code `content/quotas/`
  folder above is the better-documented, hot-reloadable version of the same idea — use the
  folder unless you have a specific reason not to.

## General patterns, reusable across all of the above

- **A `public static readonly` array/field is not actually immutable from outside code** —
  `readonly` only blocks *reassignment* from normal C# (`Tiers = newArray` won't compile
  outside the declaring class), but `FieldInfo.SetValue(null, newArray)` via reflection bypasses
  that entirely and is used throughout this project (`RefineryLine.Tiers`, `Loc.Languages`).
- **A private static backing field behind public read-only accessors** (`HandbookBook`'s
  `_book`, `ModCatalog`'s `_all`) is reachable the same way: `AccessTools.Field(type,
  "_fieldName")`, read it after whatever `EnsureLoaded()`/`Reload()`-style method has run, cast,
  mutate the live collection in place.
- **A lazy-load/cache method gets called far more often than you'd expect** (UI code re-checks
  it constantly) — any postfix hooking one needs its own guard (a tracked-ids set, or an
  id-presence check) so repeat calls don't duplicate entries.
- **When a UI method branches on "which kind is this" (`if (kind == A || kind == B) { build a
  whole section } else { build something else entirely }`), a Postfix can only fix what the
  branch actually built** — for any kind that falls into the `else`, the section you wanted
  never existed in the first place, so there's nothing to find-and-correct. Confirmed the hard
  way extending Laser's level-up UI to five more machine kinds: the hover tooltip's content was
  already generic enough to activate correctly once its one gating predicate was patched, but
  the click-popover's level-up button was built only inside a kind-specific `if`, so the other
  kinds had no button at all, not a wrong one. That case needs a Postfix that *constructs* the
  missing section itself (plain public `Button`/`Label` elements are a fine, simpler substitute
  for reflecting into a private styling helper) rather than one that corrects an existing
  element. Always check whether the UI you're extending was *built wrong* or *not built at all*
  for your new case — they need different fixes.
- **A displayed value and the value an internal gate actually checks are often read from two
  different places**, not the one method you already patched. A tooltip's price text, a
  button's own affordability check, and the actual purchase gate can each independently call a
  different private helper for "the same" number — confirmed with a third, previously-unfound
  price lookup (`RefineryScreen.LevelPrice`) that only the tooltip's price *display* read, fully
  bypassing the already-correctly-patched `State()` method real purchases went through. Grep
  every call site of the number you're trying to fix, not just the one you found first.
- **Full Prefix reimplementation is safe for small, stable, self-contained methods** (a
  one-line boolean rule, a simple getter) — risky for anything with branches/control-flow or
  likely to gain fields in a future game update. Prefer "run the original via Postfix, correct
  one known side effect after" when the method is data-driven/likely to evolve upstream.
- **A hardcoded limit is rarely hardcoded in just one place.** Every cap this project has
  raised (campaign chapter count, Refinery tier count, Refinery Laser value cap) turned out to
  be duplicated across 2-3 independent call sites — grep every consumer of a constant before
  assuming one patch covers it.
- **An "impossible" closed-enum dispatch can sometimes be aliased instead of extended.**
  Anomalies' effect dispatch is a hardcoded `switch(id)` with no generic fallback — solved not
  by adding a case, but by prefixing the dispatch method to recognize a new id and call an
  *existing* effect's handler with different params, then skip the original. Worth checking
  for the same shape before concluding something's a hard ceiling.

## Known ceilings (don't re-chase these without a genuinely new idea)

- **New weapon *families*** (not variants) don't reach the normal placement UI —
  `PartTiers.ShopShows()`'s base-arm filter excludes them, same as it excludes the game's own
  unused official variants.
- **Drone roles** (`DroneRole` enum) are reachable and live (`DroneBayScreen`'s specialise flow
  is real, not a dead label) but a closed 3-value enum with no backing definitions collection —
  ceiling is reskin/retune an existing role, not add a new one.
- **Mining-field ore tile colours** are baked into the `GraveyardOrbit/InstancedHex` shader's
  compiled program, with no exposed texture or material property — not a C#-reachable value at
  all. The only theoretical path is shipping a full replacement shader via an AssetBundle,
  which is a different order of effort (binary asset work, not Harmony patching) from
  everything else in this guide.
