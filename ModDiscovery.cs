using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace GraveFramework
{
    // One content mod found under BepInEx/plugins/. RootDir is the mod's own folder; its
    // content lives in RootDir/content/<type>/*.json.
    internal sealed class DiscoveredMod
    {
        public string Id;            // always the folder name
        public string Name;          // manifest "name"
        public string Version;       // manifest "version_number"
        public string WebsiteUrl;    // manifest "website_url"
        public string Description;   // manifest "description"
        public string[] Dependencies; // manifest "dependencies", stored as given
        public string RootDir;
    }

    // Finds every content mod sitting beside this plugin in BepInEx/plugins/. A folder
    // counts as a content mod if it has a manifest.json, or (without one) at least one
    // content/<known type>/ folder — so unrelated plugins are ignored. The result is
    // sorted by Id (ordinal, case-insensitive): the earlier mod wins an id collision.
    internal static class ModDiscovery
    {
        public static readonly string[] ContentTypes = { "structures", "mods", "boosters", "anomalies", "quotas" };

        private static List<DiscoveredMod> _current;

        // Cached result of the last Scan(). The catalog injectors read this so a full
        // pass (boot, or one Ctrl+F6 hot-reload) only touches the filesystem once;
        // Plugin.ReloadAll calls Refresh() first so new or removed mod folders get noticed.
        public static IReadOnlyList<DiscoveredMod> Current => _current ?? Refresh();

        public static IReadOnlyList<DiscoveredMod> Refresh()
        {
            _current = Scan();
            return _current;
        }

        public static List<DiscoveredMod> Scan()
        {
            var result = new List<DiscoveredMod>();
            string pluginsDir = FindPluginsDir();
            if (pluginsDir == null || !Directory.Exists(pluginsDir))
            {
                Plugin.Log.LogError($"ModDiscovery: could not locate the BepInEx plugins folder (from '{Assembly.GetExecutingAssembly().Location}').");
                return result;
            }

            foreach (string dir in Directory.GetDirectories(pluginsDir))
            {
                try
                {
                    DiscoveredMod mod = TryRead(dir);
                    if (mod != null)
                    {
                        result.Add(mod);
                    }
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"ModDiscovery: failed to read mod folder '{dir}': {e}");
                }
            }

            result.Sort((a, b) =>
            {
                int c = string.Compare(a.Id, b.Id, StringComparison.OrdinalIgnoreCase);
                return c != 0 ? c : string.Compare(a.RootDir, b.RootDir, StringComparison.OrdinalIgnoreCase);
            });

            foreach (var dup in result.GroupBy(m => m.Id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            {
                Plugin.Log.LogWarning($"ModDiscovery: mod id '{dup.Key}' is used by more than one folder ({string.Join(", ", dup.Select(m => m.RootDir))}); their content will collide.");
            }

            return result;
        }

        private static DiscoveredMod TryRead(string dir)
        {
            string folderName = Path.GetFileName(dir);
            string manifestPath = Path.Combine(dir, "manifest.json");

            ModManifest manifest = null;
            if (File.Exists(manifestPath))
            {
                try
                {
                    manifest = ModManifest.Load(manifestPath);
                }
                catch (Exception e)
                {
                    // Still a content mod (it has a manifest), just with no metadata.
                    Plugin.Log.LogError($"ModDiscovery: bad manifest '{manifestPath}' for mod '{folderName}', ignoring its fields: {e.Message}");
                    manifest = new ModManifest();
                }
            }
            else if (!ContentTypes.Any(t => Directory.Exists(Path.Combine(dir, "content", t))))
            {
                return null; // not one of ours
            }

            return new DiscoveredMod
            {
                // The id is the folder name, never read from the manifest (Thunderstore
                // manifests have no id field).
                Id = folderName,
                Name = manifest?.Name ?? "",
                Version = manifest?.VersionNumber ?? "",
                WebsiteUrl = manifest?.WebsiteUrl ?? "",
                Description = manifest?.Description ?? "",
                Dependencies = manifest?.Dependencies ?? new string[0],
                RootDir = Path.GetFullPath(dir)
            };
        }

        // Start at this DLL's folder (the framework plugin's), then walk up to the
        // folder actually named "plugins". Falls back to the DLL folder's parent if there
        // isn't one (non-standard install).
        private static string FindPluginsDir()
        {
            string ownDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            for (DirectoryInfo d = new DirectoryInfo(ownDir); d != null; d = d.Parent)
            {
                if (string.Equals(d.Name, "plugins", StringComparison.OrdinalIgnoreCase))
                {
                    return d.FullName;
                }
            }
            return Directory.GetParent(ownDir)?.FullName;
        }
    }
}
