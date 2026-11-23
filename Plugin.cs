using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using BepInEx;
using BepInEx.Logging;
using GraveyardOrbit;
using HarmonyLib;
using UnityEngine;

namespace GraveFramework
{
    // Shared library plugin: mod-folder discovery, the generic content/<type>/*.json
    // catalog injectors, the load report and the Ctrl+F6 hot-reload. Content plugins declare
    // [BepInDependency(GraveFramework.Plugin.PluginGuid)] so BepInEx loads
    // this one first.
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.luminaex.graveframework";
        public const string PluginName = "GraveFramework";
        public const string PluginVersion = "1.0.0";

        // Hot-reload hotkey — re-scans every mod's content folders and re-applies without
        // needing a game restart. Ctrl+F6 (not a bare function key) so it doesn't collide
        // with anything the game itself binds.
        private const KeyCode ReloadKey = KeyCode.F6;

        // Boot-time LoadReport: printed once, this long after Awake, by which point the
        // catalog injectors that fire during startup have run.
        private const float BootReportDelaySecs = 2f;

        // Assemblies the hot-reload scan never looks inside: the runtime, Unity, BepInEx's
        // own libraries and the game itself. Matched as a case-insensitive name prefix.
        private static readonly string[] ReloadScanSkipPrefixes =
        {
            "System", "Mono", "Unity", "mscorlib", "netstandard", "BepInEx", "0Harmony", "Newtonsoft", "Assembly-CSharp"
        };

        internal static ManualLogSource Log;

        private float _awakeTime;
        private bool _awakeTimeSet;
        private bool _bootReportPrinted;

        private void Awake()
        {
            Log = Logger;

            Harmony harmony = new Harmony(PluginGuid);
            harmony.PatchAll();

            Log.LogInfo("Harmony patches applied — content injectors (structures/mods/boosters/anomalies/quotas) active.");
            Log.LogInfo($"Hold Ctrl and press {ReloadKey} to hot-reload mod content (structures/mods/boosters/anomalies/quotas/parts) without restarting.");
        }

        private void Update()
        {
            // Time.realtimeSinceStartup is unsafe to read in Awake() — BepInEx constructs
            // plugins earlier than Unity's own boot sequence expects, and touching Time.*
            // that early crashes the native engine (confirmed: access violation inside
            // UnityPlayer during UnityEngine.Application's static init). First Update() call
            // is always safe.
            if (!_awakeTimeSet)
            {
                _awakeTimeSet = true;
                _awakeTime = Time.realtimeSinceStartup;
            }

            if (!_bootReportPrinted && Time.realtimeSinceStartup - _awakeTime >= BootReportDelaySecs)
            {
                _bootReportPrinted = true;
                Log.LogInfo(LoadReport.BuildReportText());
            }

            if ((In.KeyHeld(KeyCode.LeftControl) || In.KeyHeld(KeyCode.RightControl)) && In.KeyDown(ReloadKey))
            {
                ReloadAll();
            }
        }

        private static void ReloadAll()
        {
            Log.LogInfo("=== Hot-reload requested ===");
            // Pick up added/removed mod folders and start a fresh report for this pass.
            ModDiscovery.Refresh();
            LoadReport.Reset();
            foreach (var (name, method) in ReloadMethods)
            {
                TryReload(name, () =>
                {
                    try
                    {
                        method.Invoke(null, null);
                    }
                    catch (TargetInvocationException e) when (e.InnerException != null)
                    {
                        // Surface the injector's own exception, not reflection's wrapper.
                        ExceptionDispatchInfo.Capture(e.InnerException).Throw();
                    }
                });
            }
            Log.LogInfo("=== Hot-reload complete === (note: new languages/weapon-mechanic handlers need a restart, not hot-reloadable)");
            Log.LogInfo(LoadReport.BuildReportText());
        }

        // Every type with a static parameterless Reload(), in this plugin or in any other
        // mod assembly loaded alongside it (e.g. a content plugin's own bespoke injector),
        // takes part in Ctrl+F6 automatically: a new injector only has to define one. Found
        // once, on the first hot-reload (every plugin has loaded by then), sorted by type
        // name so the order is stable between builds.
        private static List<(string Name, MethodInfo Method)> _reloadMethods;

        private static List<(string Name, MethodInfo Method)> ReloadMethods
        {
            get
            {
                if (_reloadMethods != null)
                {
                    return _reloadMethods;
                }

                const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly;
                var found = new List<(string Name, string FullName, MethodInfo Method)>();

                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    string asmName = assembly.GetName().Name ?? "";
                    // Dynamic assemblies (Harmony/MonoMod generate these) can't enumerate types.
                    if (assembly.IsDynamic || ReloadScanSkipPrefixes.Any(p => asmName.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    Type[] types;
                    try
                    {
                        types = assembly.GetTypes();
                    }
                    catch (ReflectionTypeLoadException e)
                    {
                        types = e.Types.Where(t => t != null).ToArray();
                    }
                    catch (Exception e)
                    {
                        Log.LogWarning($"Hot-reload scan: skipping assembly '{asmName}', could not list its types: {e.Message}");
                        continue;
                    }

                    foreach (Type t in types.Where(t => !t.ContainsGenericParameters))
                    {
                        MethodInfo method = t.GetMethod("Reload", flags, null, Type.EmptyTypes, null);
                        if (method != null)
                        {
                            found.Add((t.Name, t.AssemblyQualifiedName ?? t.FullName ?? t.Name, method));
                        }
                    }
                }

                _reloadMethods = found
                    .OrderBy(x => x.Name, StringComparer.Ordinal)
                    .ThenBy(x => x.FullName, StringComparer.Ordinal)
                    .Select(x => (x.Name, x.Method))
                    .ToList();
                Log.LogInfo($"Hot-reload targets: {string.Join(", ", _reloadMethods.Select(x => $"{x.Method.DeclaringType?.Assembly.GetName().Name}:{x.Name}"))}");
                return _reloadMethods;
            }
        }

        private static void TryReload(string name, Action reload)
        {
            try
            {
                reload();
            }
            catch (Exception e)
            {
                Log.LogError($"Hot-reload of '{name}' failed: {e}");
            }
        }
    }
}
