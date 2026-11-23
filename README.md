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

Drop `GraveFramework.dll` into `BepInEx/plugins/GraveFramework/` alongside any mod that
depends on it (e.g. GraveBuilder). BepInEx loads it first automatically via
`[BepInDependency]`.

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

## Status

Early v1. Built and verified live in-game against the running game (all five catalog
injectors, cross-mod collision detection, hot-reload). Not affiliated with AnchorheadGames.
