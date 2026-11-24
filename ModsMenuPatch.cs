using System.Collections.Generic;
using GraveyardOrbit;
using GraveyardOrbit.UI;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UIElements;

namespace GraveFramework
{
    // Adds a small "MODS (n)" button to the main menu (pinned top-right of "MenuRoot",
    // the same fixed 2560x1440 design box Frame16x9.Pin gives every other menu element, so
    // it stays put across aspect ratios) that opens a read-only panel listing everything
    // ModDiscovery found under BepInEx/plugins. Lets a player see what's actually installed
    // without opening LogOutput.log. Pure UI, no gameplay effect — built the same way the
    // game's own modals are (ModalKit/MetaUi), not a hand-rolled style.
    [HarmonyPatch(typeof(MainMenuController), nameof(MainMenuController.Show))]
    internal static class ModsMenuPatch
    {
        private const string ButtonName = "GraveFramework_ModsBtn";
        private const string ModalName = "GraveFramework_ModsModal";

        private static void Postfix(MainMenuController __instance)
        {
            VisualElement root = __instance.Root;
            if (root == null)
            {
                return;
            }

            VisualElement menuRoot = root.Q<VisualElement>("MenuRoot") ?? root;

            VisualElement button = MetaUi.Key($"MODS ({ModDiscovery.Current.Count})", () => OpenPanel(root), 170f, MetaUi.Blue);
            button.name = ButtonName;
            button.style.position = Position.Absolute;
            button.style.top = 40f;
            button.style.right = 40f;
            menuRoot.Add(button);
        }

        private static void OpenPanel(VisualElement root)
        {
            if (root.Q<VisualElement>(ModalName) != null)
            {
                return; // already open
            }

            var palette = new ModalKit.Palette(MetaUi.Blue);

            VisualElement dim = new VisualElement { name = ModalName };
            dim.style.position = Position.Absolute;
            dim.style.left = 0f;
            dim.style.top = 0f;
            dim.style.right = 0f;
            dim.style.bottom = 0f;
            dim.style.alignItems = Align.Center;
            dim.style.justifyContent = Justify.Center;
            dim.style.backgroundColor = new Color(0f, 0f, 0f, 0.7f);

            VisualElement body;
            VisualElement footer;
            VisualElement card = ModalKit.DoorCard(820f, palette, "Installed Mods", 34f, out body, out footer);

            ScrollView scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.maxHeight = 560f;
            body.Add(scroll);

            IReadOnlyList<DiscoveredMod> mods = ModDiscovery.Current;
            if (mods.Count == 0)
            {
                scroll.Add(ModalKit.Copy("No mods found in BepInEx/plugins.", 18f, centre: false));
            }
            else
            {
                foreach (DiscoveredMod mod in mods)
                {
                    scroll.Add(ModRow(mod, palette.accent));
                }
            }
            MetaUi.AccentScroll(scroll, new Color(1f, 1f, 1f, 0.06f), ModalKit.Over(palette.accent, 0.45f), ModalKit.Over(palette.accent, 0.75f));

            footer.Add(ModalKit.DoorKey("CLOSE", () => dim.RemoveFromHierarchy(), palette));

            dim.Add(card);
            dim.AddToClassList("pad-modal");
            PadNav.SetModalBack(dim, () => dim.RemoveFromHierarchy());
            root.Add(dim);
            UiSfx.Wire(dim);
        }

        // One mod as the game's own list row (ModeSelectController.AddRow's recipe): a
        // chamfered MetaUi.Slab outline in the card's palette colour, brightening on hover, and
        // MetaUi.Lift's slide. No thumbnail slot: mods have no icon. The row takes the pointer
        // (Position) only so the hover works; it has no click action.
        private static VisualElement ModRow(DiscoveredMod mod, Color accent)
        {
            VisualElement row = new VisualElement { pickingMode = PickingMode.Position };
            row.style.marginBottom = 10f;
            row.style.paddingLeft = 22f;
            row.style.paddingRight = 22f;
            row.style.paddingTop = 14f;
            row.style.paddingBottom = 14f;
            SlabFrame frame = MetaUi.Slab(row, accent, dim: false, 13f);
            MetaUi.PaintSlab(frame, accent, dim: false, 0.45f);
            row.RegisterCallback<PointerEnterEvent>(_ => MetaUi.PaintSlab(frame, accent, dim: false, 0.85f));
            row.RegisterCallback<PointerLeaveEvent>(_ => MetaUi.PaintSlab(frame, accent, dim: false, 0.45f));
            MetaUi.Lift(row, slide: true);

            VisualElement header = new VisualElement { pickingMode = PickingMode.Ignore };
            header.style.flexDirection = FlexDirection.Row;
            header.style.justifyContent = Justify.SpaceBetween;
            string title = string.IsNullOrWhiteSpace(mod.Name) ? mod.Id : mod.Name;
            header.Add(MetaUi.Text(title.ToUpperInvariant(), MetaUi.Face.DisplayBold, 22f, MetaUi.TextTone(accent), 2f));
            if (!string.IsNullOrWhiteSpace(mod.Version))
            {
                header.Add(MetaUi.Text("v" + mod.Version, MetaUi.Face.Body, 16f, MetaUi.Fade(MetaUi.Ink, 0.6f)));
            }
            row.Add(header);

            if (!string.IsNullOrWhiteSpace(mod.Description))
            {
                row.Add(ModalKit.Copy(mod.Description, 16f, centre: false, 0.8f));
            }

            if (mod.Dependencies != null && mod.Dependencies.Length > 0)
            {
                row.Add(MetaUi.Text("Requires: " + string.Join(", ", mod.Dependencies), MetaUi.Face.Body, 14f, MetaUi.Fade(MetaUi.Ink, 0.5f), 0f, wrap: true));
            }

            return row;
        }
    }
}
