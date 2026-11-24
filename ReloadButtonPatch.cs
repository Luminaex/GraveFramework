using System.Reflection;
using GraveyardOrbit.UI;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UIElements;

namespace GraveFramework
{
    // Replaces the old Ctrl+F6 hotkey with a visible "RELOAD MODS" button on the Contracts/
    // mode-select screen — discoverable without reading a README, and doesn't fight the game's
    // own key bindings. Harmony-postfixes ModeSelectController.Show (public, runs after its UI
    // is fully built) and reflects into its private _doc field (same technique GraveBuilder's
    // own MainMenuBuilderButtonPatch uses for MainMenuController) since there's no public Root
    // accessor here the way MainMenuController has one. Shown on every mode-select tab (Home/
    // Campaign/Contracts/Records), not gated to Contracts specifically — reloading mod content
    // is harmless from any tab, and gating it to one internal tab-state would need reflecting
    // into private tab-tracking fields this patch doesn't otherwise need.
    [HarmonyPatch(typeof(ModeSelectController), nameof(ModeSelectController.Show))]
    internal static class ReloadButtonPatch
    {
        private const string ButtonName = "GraveFramework_ReloadBtn";
        private static readonly FieldInfo DocField = AccessTools.Field(typeof(ModeSelectController), "_doc");

        private static void Postfix(ModeSelectController __instance)
        {
            VisualElement root = (DocField?.GetValue(__instance) as UIDocument)?.rootVisualElement;
            if (root == null || root.Q<VisualElement>(ButtonName) != null)
            {
                return;
            }

            VisualElement wrap = MetaUi.Key("RELOAD MODS", null, 220f, MetaUi.Teal);
            wrap.name = ButtonName;
            wrap.style.position = Position.Absolute;
            wrap.style.top = 40f;
            wrap.style.right = 40f;
            Button inner = wrap.Q<Button>();
            if (inner != null)
            {
                inner.clicked += () =>
                {
                    Plugin.ReloadAll();
                    // Brief feedback — there's no toast/notification system to hook into here,
                    // so just flash the button's own label.
                    inner.text = "RELOADED";
                    wrap.schedule.Execute(() => inner.text = "RELOAD MODS").ExecuteLater(1200);
                };
            }
            root.Add(wrap);
        }
    }
}
