# Changelog

## v1.1.0

- Moved the RELOAD MODS button off the Contracts page and into the main menu's Mods panel
  (opened via the "MODS (n)" button), alongside the installed-mods list it already showed.

## v1.0.0

Initial release.

- Mod discovery: any folder under `BepInEx/plugins/` with a `manifest.json` or a `content/<type>/`
  folder is picked up as a content mod automatically, no DLL required.
- Five content types: `content/structures/`, `content/mods/`, `content/boosters/`,
  `content/anomalies/`, `content/quotas/`.
- Cross-mod collision detection on content ids, with both mods named in the log.
- Boot-time load report in `BepInEx/LogOutput.log`.
- A "MODS (n)" button on the main menu listing every discovered mod, with a "RELOAD MODS"
  button to hot-reload content without restarting.
