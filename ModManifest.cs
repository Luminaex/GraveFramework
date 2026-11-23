using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace GraveyardOrbitModdingFramework
{
    // A content mod's self-description, read from <modFolder>/manifest.json. Same shape as
    // a Thunderstore package manifest:
    //   {"name":"MyMod","version_number":"1.0.0","website_url":"","description":"...",
    //    "dependencies":["Luminaex-GraveyardOrbitModdingFramework-1.0.0"]}
    // Every field is optional here. There is no id or author field: the mod's id is always
    // its folder name (see ModDiscovery), and Thunderstore keeps the author/namespace in the
    // package's identity, not in the manifest. Dependencies are stored as given, never
    // validated or resolved.
    internal sealed class ModManifest
    {
        public string Name;
        public string VersionNumber;
        public string WebsiteUrl;
        public string Description;
        public string[] Dependencies = new string[0];

        // Returns null if the file doesn't exist. A malformed file throws (JSON parse
        // error) so the caller can log it against the right folder.
        public static ModManifest Load(string manifestPath)
        {
            if (!File.Exists(manifestPath))
            {
                return null;
            }

            JObject o = JObject.Parse(File.ReadAllText(manifestPath));
            return new ModManifest
            {
                Name = (string)o["name"],
                VersionNumber = (string)o["version_number"],
                WebsiteUrl = (string)o["website_url"],
                Description = (string)o["description"],
                Dependencies = o["dependencies"] is JArray deps
                    ? deps.Select(d => (string)d).Where(d => !string.IsNullOrWhiteSpace(d)).ToArray()
                    : new string[0]
            };
        }
    }
}
