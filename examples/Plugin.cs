using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace GraveFrameworkExamples
{
    // Reference-only companion to GraveFramework: the code-based techniques the zero-code
    // content folders don't cover (new campaign chapter, new ship part, new weapon
    // mechanic, mod-supported localisation). The content/ folder next to this plugin is
    // picked up by GraveFramework's own discovery, no code involved. Not for players.
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(GraveFramework.Plugin.PluginGuid)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.luminaex.graveframeworkexamples";
        public const string PluginName = "GraveFrameworkExamples";
        public const string PluginVersion = "1.0.0";

        internal static ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;
            Harmony harmony = new Harmony(PluginGuid);
            harmony.PatchAll();
            Log.LogInfo("GraveFrameworkExamples: example patches applied (campaign chapter, parts, weapon mechanic, localisation).");
        }
    }
}
