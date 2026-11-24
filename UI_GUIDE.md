# Building menu UI — a guide for mod makers

Graveyard Orbit's menus are all [Unity UI Toolkit](https://docs.unity3d.com/Manual/UIElements.html)
(`UnityEngine.UIElements` — `VisualElement`/`Button`/`Label`/`ScrollView`, not legacy `OnGUI` or
world-space canvases). The game ships its own small UI helper library
(`GraveyardOrbit.UI` — `MetaUi`, `ModalKit`, `SlabFrame`, `PadNav`, `UiSfx`, …) that every
official screen (Refinery, Boosters, Quotas, the pause menu, the main menu itself) is built
from. **A mod can call all of it** — it's all `public`. Building your own menu addition means
reusing those same pieces, not rolling your own look from scratch.

`ModsMenuPatch.cs` in this repo (the main-menu "MODS (n)" button + installed-mods list) is a
complete, working, in-game-verified example of everything below — read it side by side with
this guide. It's also the smallest realistic template to copy if you just want "a button that
opens a panel with some text in it."

## 1. Opt in to the UI Toolkit references

`Directory.Build.props` only wires up `UnityEngine.UIElementsModule` /
`TextRenderingModule` / `TextCoreTextEngineModule` for projects that ask for them (kept opt-in
on purpose — most content mods never touch UI). Add this to your `.csproj`'s first
`<PropertyGroup>`:

```xml
<UseUnityUI>true</UseUnityUI>
```

## 2. Finding a screen to hook into

There's no event bus for "a screen opened" — you Harmony-patch the screen class's own
build method, the same way you'd patch any other game logic. Pick a **public** method so a
Postfix can read its parameters/results cleanly (`MainMenuController.Show`,
`PauseMenu` methods, etc. are all public); private methods still work as Harmony targets if
you need one, just via the string-name `[HarmonyPatch(typeof(X), "MethodName")]` form instead
of `nameof`.

```csharp
[HarmonyPatch(typeof(MainMenuController), nameof(MainMenuController.Show))]
internal static class MyMenuPatch
{
    private static void Postfix(MainMenuController __instance)
    {
        VisualElement root = __instance.Root; // null if Show() bailed early (missing asset) — always check
        if (root == null) return;
        // ... build your UI and Add() it to root (or one of its children) here
    }
}
```

Postfix (not Prefix) — you want the screen fully built first, then you add to it. Grab the
instance's own `VisualElement` root rather than searching `Resources`/`UIDocument` yourself;
every screen controller exposes one (`.Root`, or a private `UIDocument` field reachable via
Harmony's `___fieldName` injection if there's no public accessor).

**Screens that rebuild themselves don't keep your addition.** `MainMenuController.Show()`
tears down and rebuilds the whole tree each time the title screen is (re)shown — your Postfix
runs again each time too, so this is actually fine *as long as your code is idempotent* (just
adds its one button again to the freshly-built tree). Don't cache a `VisualElement` reference
in a static field across screen rebuilds; the old one is destroyed garbage by the time you'd
reuse it.

## 3. The fixed-coordinate design canvas (`MenuRoot`)

Full-screen menus pin a child element (conventionally named `"MenuRoot"`) to a fixed
**2560×1440** virtual canvas via `Frame16x9.Pin(el, 2560f, 1440f)` — centred, scaled to fit,
letterboxed on mismatched aspect ratios. If that element already exists in the screen you're
extending (query it: `root.Q<VisualElement>("MenuRoot")`), **add your own UI as a child of it
and use plain pixel coordinates in that space** — `top: 40f, right: 40f` means the same place
on a 16:9 ultrawide as on a 16:10 laptop. Positioning against the raw `rootVisualElement`
instead means fighting real screen-resolution-dependent coordinates.

```csharp
VisualElement menuRoot = root.Q<VisualElement>("MenuRoot") ?? root; // fall back if the screen has no such element
VisualElement myButton = MetaUi.Key("MY BUTTON", OnClick, 170f);
myButton.style.position = Position.Absolute;
myButton.style.top = 40f;
myButton.style.right = 40f;
menuRoot.Add(myButton);
```

Z-order in UI Toolkit is **add order** — the last child added renders on top. Modals rely on
this: add the dim/backdrop layer last, after everything else already on screen.

## 4. The helper library (`GraveyardOrbit.UI`), by what you'd reach for

All of these are already used throughout the shipped game — pick the one doing the closest
job to what you want rather than writing raw `VisualElement` styling, so your UI matches the
game's own look for free.

### `MetaUi` — text, buttons, rows, scrollbars
- `MetaUi.Text(text, Face, size, color, tracking, wrap)` — a styled, non-interactive `Label`
  (correct font asset per `Face`: `Display`/`DisplayBold`/`Body`/`BodyBold`). Use this instead
  of a bare `new Label()` for anything the player reads.
- `MetaUi.Key(text, onClick, width, family?)` / `MetaUi.Primary(...)` — a full button with the
  game's slab-frame chrome, hover/press states and colour theming. `Key` is the compact form
  (good for a corner button); `Primary` is the big call-to-action form (good for a modal's main
  action).
- `MetaUi.TableRow(host, key, value, valueColor?)` — a label/value row with a bottom divider
  (stat displays, settings rows).
- `MetaUi.Bullet(host, text)` / `MetaUi.Callout(host, text, Tone)` — a bulleted line, or a
  highlighted warning/info/success callout box.
- `MetaUi.Panel(family, notch, dim, padding)` — a chrome'd container `VisualElement` (same
  slab-frame look as a button, but as a plain panel) if you need a styled box that isn't a
  modal.
- `MetaUi.AccentScroll(scroll, trackColor, thumbColor, cueColor, cue?)` — gives a `ScrollView`
  the game's own themed custom scrollbar instead of Unity's default one. Call it once after
  adding your content.
- Colour constants (`MetaUi.Orange`, `.Blue`, `.Teal`, `.Ink`, …) and `MetaUi.Fade(color, a)` /
  `.Light(color)` for theming — reuse these instead of inventing new hex values so your UI
  colour-matches the surrounding game.

### `ModalKit` — popup panels
- `ModalKit.DoorCard(width, palette, title, titleSize, out body, out footer)` — the standard
  chamfered popup card shell (header/body/footer regions already laid out and styled). This is
  what every confirm dialog, announcement and info panel in the game actually is. Add your own
  content into the returned `body`/`footer`.
- `ModalKit.Palette(accentColor)` — bundles an accent colour with its derived light/text/CTA
  shades, passed into most `ModalKit`/button calls so a whole modal stays colour-consistent
  from one constructor call.
- `ModalKit.DoorKey(text, onClick, palette, primary?, pad?)` — a footer action button matching
  the card's chrome (the `pad` param shows a gamepad glyph hint when a controller is active).
- `ModalKit.Confirm(question, consequence, safeText, onSafe, dangerText, onDanger, width?)` —
  a complete ready-made "are you sure?" two-button dialog; if that's literally your use case,
  call this instead of assembling `DoorCard` + two `DoorKey`s yourself.
- `ModalKit.Copy(text, size, centre, alpha)` / `ModalKit.Rules(lines, palette)` — body text and
  a bulleted-rule-list block, styled to match the card.

### `SlabFrame` — the chrome itself
The notched-corner background panel used behind almost every button/card/panel above. You
rarely construct one directly (the helpers above already do), but if you're styling a raw
`VisualElement` yourself: `new SlabFrame { Notch, Stroke, StrokeColor, FillColor, TintColor
}.Behind(myElement)` paints it as `myElement`'s background.

### `PadNav` — gamepad support
- `PadNav.SetModalBack(myModalRoot, onClose)` — wires the controller's Back/B button to close
  your modal. Call this for every modal you open; skipping it leaves gamepad players stuck.
- `PadNav.FocusOn(element)` — moves gamepad focus to a specific element (e.g. a modal's default
  button) once it's visible.
- `PadNav.Glyph(name)` — looks up a button-prompt texture (`"button_x"`, `"button_y"`, …) for
  the player's actual connected controller type, if you want to show an icon hint.

### `UiSfx` — sound
- `UiSfx.Wire(root)` — call once on any new UI subtree (a panel, a modal) to wire up the game's
  own hover/click sounds on its buttons automatically. Without this, your buttons work but are
  silent while every other button in the game clicks/hovers audibly — call it, it's one line.

## 5. The modal open/close pattern

Every popup in the game (confirm dialogs, announcements, the pause menu's settings panel) is
the same shape — a full-screen dim backdrop holding a centred card:

```csharp
private static void OpenPanel(VisualElement root)
{
    if (root.Q<VisualElement>("MyModal") != null) return; // guard: don't stack a second copy

    var palette = new ModalKit.Palette(MetaUi.Blue);

    VisualElement dim = new VisualElement { name = "MyModal" };
    dim.style.position = Position.Absolute;
    dim.style.left = 0f; dim.style.top = 0f; dim.style.right = 0f; dim.style.bottom = 0f;
    dim.style.alignItems = Align.Center;
    dim.style.justifyContent = Justify.Center;
    dim.style.backgroundColor = new Color(0f, 0f, 0f, 0.7f);

    VisualElement body, footer;
    VisualElement card = ModalKit.DoorCard(820f, palette, "My Panel", 34f, out body, out footer);
    body.Add(MetaUi.Text("Hello from my mod!", MetaUi.Face.Body, 18f, MetaUi.Ink));
    footer.Add(ModalKit.DoorKey("CLOSE", () => dim.RemoveFromHierarchy(), palette));

    dim.Add(card);
    dim.AddToClassList("pad-modal");
    PadNav.SetModalBack(dim, () => dim.RemoveFromHierarchy());
    root.Add(dim);
    UiSfx.Wire(dim);
}
```

Closing is just `dim.RemoveFromHierarchy()` — UI Toolkit has no separate "destroy," removing
from the tree is enough (the GC collects it once nothing references it, same as any other
C# object).

## 6. Gotchas

- **`pickingMode`** — `VisualElement`s default to `PickingMode.Position` (clickable/hoverable).
  Decorative elements you build by hand (a wrapper `VisualElement`, a label that's just part of
  a button's visual, like `MetaUi.Text`'s output) should set `pickingMode = PickingMode.Ignore`
  so they don't steal clicks meant for something behind/around them — every helper above
  already does this correctly for its own output; only matters if you build raw elements
  yourself.
- **A `Label` isn't a `Button`.** `MetaUi.Text` returns a `Label` with no click handling at all.
  If you want clickable text, wrap it in an actual `Button` (see how `MetaUi.Key`/`Primary` do
  it) rather than registering a `PointerDownEvent` callback on a bare `Label` — you'll also
  lose the game's own hover/press visual feedback and gamepad focus support that way.
  `UnityEngine.UIElements.Button` renders text "as itself" if you set `.text` directly, but
  `MetaUi.Key` gives you the game's actual button chrome for free, so prefer it.
- **Re-entrant `EnsureLoaded()`/`Show()`-style methods fire more than once.** Same lesson as
  the content-injection techniques in `MODDING_GUIDE.md` — if your Postfix adds something and
  the patched method can run again (a screen re-shown, a lazy cache re-checked), guard against
  duplicates by querying for a named element first (`root.Q<VisualElement>("MyThing") != null`)
  before building a second copy.
- **Don't guess a corner that's already in use.** `MainMenuController` already occupies several
  fixed positions (`CornerLeftCaption`, `CornerRight`, `VersionLabel`, the two store badge
  buttons) that aren't all visible in the decompiled source's plain reading — **check your
  addition in-game** before assuming a given `top`/`right` is actually empty space; this is
  exactly the "looks right from reading, wrong when tested" trap the rest of this project has
  hit repeatedly (see `MODDING_GUIDE.md`'s general patterns). `ModsMenuPatch.cs`'s
  `top: 40, right: 40` placement is a reasonable first guess (that corner is normally empty
  outside demo builds), not a verified-safe spot — nudge the margins if it overlaps something
  for you.
