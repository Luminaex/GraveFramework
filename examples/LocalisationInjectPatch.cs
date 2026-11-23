using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GraveyardOrbit;
using HarmonyLib;
using Newtonsoft.Json.Linq;

namespace GraveFrameworkExamples
{
    // Adds mod-supported languages. Loc.Languages is public static readonly (readonly only
    // blocks normal reassignment, not FieldInfo.SetValue — same bypass as RefineryLine.Tiers)
    // so new Language entries can be appended directly, no Harmony patch needed for that
    // part. The harder part: Loc.Load(Language) only ever reads a baked
    // Resources/loc/<code>.json TextAsset, which can't exist for a code that didn't ship —
    // and its private Dictionary<string, Entry> _table (Entry is itself a private nested
    // struct) can't be populated with normal code from outside Loc at all. Both are reached
    // via reflection: AccessTools.Inner for the private Entry type, then
    // Activator.CreateInstance + FieldInfo.SetValue to build each entry, and a reflectively
    // constructed Dictionary<string, Entry> (via Type.MakeGenericType) set onto _table.
    [HarmonyPatch(typeof(GameConfig), "Load")]
    internal static class LanguagesInjectPatch
    {
        private static bool _done;

        private static void Postfix()
        {
            if (_done)
            {
                return;
            }
            _done = true;

            try
            {
                string pluginDir = Path.GetDirectoryName(typeof(LanguagesInjectPatch).Assembly.Location);
                string dir = Path.Combine(pluginDir, "new_languages");
                if (!Directory.Exists(dir))
                {
                    return;
                }

                var oldLangs = Loc.Languages;
                var newEntries = new List<Loc.Language>();

                foreach (string file in Directory.GetFiles(dir, "*.json"))
                {
                    JObject o = JObject.Parse(File.ReadAllText(file));
                    string code = (string)o["code"];
                    if (string.IsNullOrEmpty(code))
                    {
                        Plugin.Log.LogError($"NewLanguages: '{file}' has no 'code', skipping.");
                        continue;
                    }
                    if (Array.Exists(oldLangs, l => l.code == code))
                    {
                        Plugin.Log.LogError($"NewLanguages: '{code}' already exists, skipping ({file}).");
                        continue;
                    }

                    newEntries.Add(new Loc.Language
                    {
                        code = code,
                        name = (string)o["name"] ?? code,
                        english = (string)o["english"] ?? code
                    });
                    Plugin.Log.LogInfo($"NewLanguages: registered language '{code}' ({(string)o["name"]}).");
                }

                if (newEntries.Count > 0)
                {
                    var combined = new Loc.Language[oldLangs.Length + newEntries.Count];
                    Array.Copy(oldLangs, combined, oldLangs.Length);
                    for (int i = 0; i < newEntries.Count; i++)
                    {
                        combined[oldLangs.Length + i] = newEntries[i];
                    }
                    AccessTools.Field(typeof(Loc), "Languages").SetValue(null, combined);
                    Plugin.Log.LogInfo($"NewLanguages: Loc.Languages extended to {combined.Length} entries.");
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"NewLanguages injection failed: {e}");
            }
        }
    }

    [HarmonyPatch(typeof(Loc), "Load")]
    internal static class LocLoadPatch
    {
        private static readonly Type EntryType = AccessTools.Inner(typeof(Loc), "Entry");
        private static readonly FieldInfo EntryTField = AccessTools.Field(EntryType, "t");
        private static readonly FieldInfo EntrySrcField = AccessTools.Field(EntryType, "src");
        private static readonly FieldInfo TableField = AccessTools.Field(typeof(Loc), "_table");
        private static readonly Type DictType = typeof(Dictionary<,>).MakeGenericType(typeof(string), EntryType);
        private static readonly MethodInfo DictAdd = DictType.GetMethod("Add");

        private static bool Prefix(Loc.Language lang)
        {
            try
            {
                string pluginDir = Path.GetDirectoryName(typeof(LocLoadPatch).Assembly.Location);
                string path = Path.Combine(pluginDir, "new_languages", lang.code + ".json");
                if (!File.Exists(path))
                {
                    return true; // not one of ours — let the original Resources-based Load run
                }

                JObject o = JObject.Parse(File.ReadAllText(path));
                if (!(o["strings"] is JObject strings))
                {
                    Plugin.Log.LogError($"NewLanguages: '{path}' has no 'strings' object.");
                    return false;
                }

                object dict = Activator.CreateInstance(DictType);
                int count = 0;
                foreach (JProperty prop in strings.Properties())
                {
                    object entry = Activator.CreateInstance(EntryType);
                    EntryTField.SetValue(entry, (string)prop.Value);
                    EntrySrcField.SetValue(entry, null);
                    DictAdd.Invoke(dict, new object[] { prop.Name, entry });
                    count++;
                }

                TableField.SetValue(null, dict);
                Plugin.Log.LogInfo($"NewLanguages: loaded {count} strings for '{lang.code}' from {path}.");
                return false; // skip the original Resources-based Load entirely
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"NewLanguages Load patch failed for '{lang.code}': {e}");
                return true;
            }
        }
    }
}
