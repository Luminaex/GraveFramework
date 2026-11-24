# GraveFrameworkExamples

A companion reference project for [GraveFramework](../README.md) that shows every technique the
framework enables, working end to end.

**This is for mod developers, not players.** Read it, build it yourself, and copy whatever
pattern you need. It's a separate, optional DLL (`GraveFrameworkExamples.dll`) and is never
bundled into GraveFramework's own release zip.

**Start with [`MODDING_GUIDE.md`](MODDING_GUIDE.md)** — a consolidated reference tying every
technique below together in one place, plus two techniques that don't need a demo file at all
(a one-line QoL toggle, reusing an existing public catalog list directly), the general
reflection/patching patterns that repeat across all of them, and the known hard ceilings not
worth re-chasing without a genuinely new idea.

## What's in here

### Zero-code content folders (just JSON, no DLL needed)

GraveFramework's mod discovery picks these up from any plugin folder with a `manifest.json` or a
`content/<type>/` folder. Nothing in this project's code is involved.

| File | What it adds |
| --- | --- |
| `manifest.json` | Identifies the example folder as a content mod. |
| `content/mods/l_prism_shot.json` | A new research-tree "Mod" node. |
| `content/mods/shockwave.json` | The research-tree node that the weapon-mechanic example below hooks into. |
| `content/boosters/mod_test_booster.json` | A new booster. |
| `content/anomalies/mod_test_anomaly.json` | A new anomaly. |
| `content/quotas/destroy_6000.json` | A new quota. |

### Code-based techniques (what the content folders don't cover)

Each `*Patch.cs` file has a long comment at the top explaining what it hooks and why.

| File | Technique | Data it reads |
| --- | --- | --- |
| `NewCampaignChapterPatch.cs` | A new campaign chapter integrated into the real Campaign Hub (Classic track only). | `new_structures_source/chapter6.json`, `chapter6_cells.json` |
| `NewPartsPatch.cs` | A new ship part (a laser variant) injected into the live `GameData`, reusing an existing part's behaviour and model. | `new_parts_source/laser_sniper.json` |
| `NewWeaponMechanicPatch.cs` | A genuinely new weapon hit-mechanic (an AoE "shockwave" nova) registered through the game's own `ModRuntime` dispatch. | `content/mods/shockwave.json` (the node) |
| `LocalisationInjectPatch.cs` | Mod-supported localisation: a whole new language added to `Loc.Languages` and loaded from a mod file. | `new_languages_source/pirate.json` |
| `HandbookInjectPatch.cs` | A mod documenting its own content in the in-game Handbook. **Not yet verified live in-game** — built, not smoke-tested. | none (entries are hardcoded in the patch) |

`Plugin.cs` declares `[BepInDependency(GraveFramework.Plugin.PluginGuid)]` and calls
`Harmony.PatchAll()`, which picks up every `[HarmonyPatch]` class in the assembly.

## Building and trying it

Requires the .NET SDK and a local install of Graveyard Orbit. This project references
`..\GraveFramework.csproj`, so build it from inside a GraveFramework checkout. Edit the
`$(GameDir)` property in the GraveFramework checkout's root `Directory.Build.props` (shared by
both projects) to point at your own install, then `dotnet build`.

To see it running, copy `GraveFrameworkExamples.dll`, `manifest.json` and `content/` into
`BepInEx/plugins/GraveFrameworkExamples/`, alongside an installed GraveFramework. Copy the three
`new_*_source/` folders there too, but drop the `_source` suffix: the patches read
`new_structures/`, `new_parts/` and `new_languages/` next to the DLL. New languages and the weapon-mechanic handler need a game restart. The
`content/` folders hot-reload via the RELOAD MODS button on the Contracts page.
