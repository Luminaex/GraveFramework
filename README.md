# GraveFramework

A shared [BepInEx](https://github.com/BepInEx/BepInEx)/[Harmony](https://github.com/pardeike/Harmony)
plugin for [Graveyard Orbit](https://store.steampowered.com/app/4654120) (dev AnchorheadGames)
that lets other mods add real content — new structures/contracts, research-tree "Mods", boosters,
anomalies and quotas — by dropping JSON files into a folder, with no code required.

This is **not a gameplay mod on its own**. It's infrastructure other mods depend on.
[GraveBuilder](https://github.com/Luminaex/GraveBuilder) is the first mod built on top of it.

## What it does

- **Mod discovery** — any folder under `BepInEx/plugins/` with a `manifest.json` or a
  `content/<type>/` folder is picked up as a content mod automatically, DLL optional.
- **Five content types**: `content/structures/`, `content/mods/`, `content/boosters/`,
  `content/anomalies/`, `content/quotas/` — each a folder of `*.json` files matching the
  game's own schema for that catalog.
- **Cross-mod collision detection** — if two mods claim the same id, the later one is rejected
  and the log names both mods, instead of silently overwriting.
- **A boot-time load report** — printed to `BepInEx/LogOutput.log` a couple of seconds after
  launch, and again after every hot-reload: every discovered mod, and per-type
  injected/skipped/rejected counts with reasons.
- **Hot-reload** — hold `Ctrl` and press `F6` in-game to re-scan every mod's content folders
  and re-apply without restarting. Any assembly's own bespoke injector also joins in
  automatically, just by defining a static parameterless `Reload()` method — no registration
  needed.

## Installing (for players)

**Recommended:** install via [r2modman](https://thunderstore.io/package/ebkr/r2modman/) or the
Thunderstore Mod Manager — search for "GraveFramework" under Graveyard Orbit and click Install.
BepInEx and any dependency are installed automatically.

**Manual install:**
1. Install [BepInEx 5.4.x (Mono)](https://github.com/BepInEx/BepInEx/releases) into your
   Graveyard Orbit install, if you haven't already — extract its zip into the game's install
   folder and run the game once so it finishes setting itself up.
2. Download the latest release zip from this repo's
   [Releases](https://github.com/Luminaex/GraveFramework/releases) page and extract it into
   `BepInEx/plugins/`, so you end up with `BepInEx/plugins/GraveFramework/GraveFramework.dll`.
   BepInEx loads it first automatically via `[BepInDependency]` for any mod that depends on it
   (e.g. GraveBuilder).

## Building from source

Requires the .NET SDK and a local install of Graveyard Orbit (for its `Assembly-CSharp.dll`
and BepInEx's own core DLLs, referenced via `HintPath`). Edit the `$(GameDir)` property at the
top of `GraveFramework.csproj` to point at your own install, e.g.:

```xml
<GameDir>C:\Program Files (x86)\Steam\steamapps\common\Graveyard Orbit</GameDir>
```

Then `dotnet build`.

## Authoring content for this framework

Each content type's JSON schema matches the game's own data format for that catalog — see
the comments in the matching `*InjectPatch.cs` file in this repo for the exact fields expected
(e.g. `ModsInjectPatch.cs` for research-tree nodes, `StructuresInjectPatch.cs` for
contracts/structures). A `manifest.json` identifying your mod is optional but recommended:

```json
{
  "name": "YourModName",
  "version_number": "1.0.0",
  "website_url": "",
  "description": "",
  "dependencies": []
}
```

## Examples

The [`examples/`](examples/) folder is a separate, optional companion project
(`GraveFrameworkExamples.dll`) that shows every technique this framework enables, working end
to end: zero-code content folders (a research-tree node, a booster, an anomaly, a quota) and
code-based techniques the content folders don't cover (a new campaign chapter, a new ship
part, a new weapon hit-mechanic, mod-supported localisation). It's reference material for mod
developers to read and build themselves. It isn't something players install, and it's never
included in GraveFramework's own release zip. See [`examples/README.md`](examples/README.md).

## Status

Early v1. Built and verified live in-game against the running game (all five catalog
injectors, cross-mod collision detection, hot-reload). Not affiliated with AnchorheadGames.
