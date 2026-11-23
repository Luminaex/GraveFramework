using System;
using System.IO;
using GraveyardOrbit;
using GraveyardOrbit.UI;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace GraveFrameworkExamples
{
    // The deepest patch in the project: a genuinely new campaign chapter, integrated into
    // the real Campaign Hub (not just an Overtime-style contract).
    //
    // 2026-11-21: the game shipped a campaign-structure update the same day this was being
    // built (added a second track, CampaignTrack.OneShip, alongside the original "Classic"
    // 5-chapter track; replaced the old globe/starmap hub with a horizontal card layout).
    // That update happened to CENTRALISE what used to be five independent hardcoded "5"
    // checks into a single method, CampaignState.ChapterCount(CampaignTrack) — so this file
    // is now simpler than before, not more complicated, once rewritten against the new
    // layout. Everything here is re-verified against the live decompile taken the same day
    // (see D:\GameCommunityMod\decompiled vs decompiled_old_2026-11-20 for the diff).
    //
    // Guiding rule: only ever touch the Classic track. OneShip is a separate, unrelated
    // 3-act mode and every patch below checks CampaignState.Track (or the track argument it
    // was given) before doing anything.
    internal static class NewCampaignChapterPatch
    {
        private const string ChapterJsonFileName = "chapter6.json";

        // Computed immediately via static field initializer — StructureCatalog.I's result
        // and StoryScript's cached beats both get reused for the life of the process, so
        // _pluginDir must never be null the first time any of our patches check it.
        private static readonly string _pluginDir = Path.GetDirectoryName(typeof(NewCampaignChapterPatch).Assembly.Location);
        private static string ChapterJsonPath => Path.Combine(_pluginDir, "new_structures", ChapterJsonFileName);

        // The one real "5" that mattered: CampaignState.Chapters, Finished, FinishedOf,
        // OnContractRecorded, NodeStates' count, and CampaignHubPage.StarMap's card count
        // all now derive from this single method — so patching it here is enough to make
        // the campaign correctly believe there are 6 Classic chapters everywhere at once.
        [HarmonyPatch(typeof(CampaignState), "ChapterCount")]
        internal static class ChapterCountPatch
        {
            private static bool Prefix(CampaignTrack t, ref int __result)
            {
                if (t != CampaignTrack.Classic)
                {
                    return true; // OneShip (3 acts) — untouched
                }
                __result = 6;
                return false;
            }
        }

        // ChapterColour(int,float) hardcodes "index 4 is the finale" — the last chapter gets
        // a special animated-rainbow colour instead of one fixed spectrum colour. That needs
        // to track whichever index is actually last now (6 chapters → index 5), for whichever
        // track is currently showing. The old build read this bound off a Points[] field that
        // no longer exists on CampaignHubPage at all post-update — this reimplementation reads
        // it from CampaignState.ChapterCount(CampaignState.Track) instead, so it keeps working
        // even if the UI changes shape again. Method body otherwise copied verbatim from the
        // live decompile.
        [HarmonyPatch(typeof(CampaignHubPage), "ChapterColour")]
        internal static class ChapterColourFinalePatch
        {
            private static bool Prefix(int i, float anim, ref Color __result)
            {
                int lastIndex = CampaignState.ChapterCount(CampaignState.Track) - 1;
                if (i < lastIndex)
                {
                    __result = TerminalUi.Spectrum[i];
                    return false;
                }
                Color[] spectrum = TerminalUi.Spectrum;
                float num = anim * 1.2f;
                int num2 = (int)num;
                __result = Color.Lerp(spectrum[num2 % 4], spectrum[(num2 + 1) % 4], num - (float)num2);
                return false;
            }
        }

        // CardColour(int) (private, drives the hub card's border/fill colour) independently
        // hardcodes the same "index 4" threshold for picking the static finale tint. Same
        // dynamic-threshold fix, same reasoning — purely cosmetic (which card gets the
        // special tint), zero crash risk either way since it's just colour maths now, not
        // array bounds.
        [HarmonyPatch(typeof(CampaignHubPage), "CardColour")]
        internal static class CardColourPatch
        {
            private static bool Prefix(int i, ref Color __result)
            {
                if (i == 0)
                {
                    __result = TerminalUi.Orange;
                    return false;
                }
                int lastIndex = CampaignState.ChapterCount(CampaignState.Track) - 1;
                if (i >= lastIndex)
                {
                    __result = CampaignHubPage.ChapterColour(lastIndex, 0f);
                    return false;
                }
                __result = Color.Lerp(TerminalUi.Spectrum[i], Color.white, 0.25f);
                return false;
            }
        }

        // Card()'s footer row lays the "CHAPTER N" title and the "CLEARED"/"IN PROGRESS"/
        // "LOCKED" tag out with Justify.SpaceBetween and no overflow handling — fine at the
        // vanilla 5-wide packing (confirmed against a mod-disabled baseline screenshot: no
        // overlap there), but 6 cards makes StarMap's own per-card width shrink far enough
        // that the longer tag text ("CLEARED", "IN PROGRESS") overlaps the title instead of
        // sitting beside it ("LOCKED" is short enough to still fit). ModeSelectController
        // already has a precedent for exactly this — FitPageName() measures text and shrinks
        // font size to fit — so this reuses that approach instead of reimplementing Card()
        // (a long method building the whole card visual) just to add a gap.
        [HarmonyPatch(typeof(CampaignHubPage), "Card")]
        internal static class CardLabelFitPatch
        {
            private const float TitleBaseSize = 34f;
            private const float TitleFloorSize = 20f;
            private const float MinGap = 10f;
            private const int MaxPasses = 5;

            // Two earlier versions of this fix tried to PREDICT the overlap from
            // MeasureTextSize plus MetaUi.Text's own letterSpacing/paddingRight maths — the
            // first missed that tracking adds (text.Length-1)*tracking of extra width
            // MeasureTextSize never reports (fixed "CLEARED"/"LOCKED" but not the longer
            // "IN PROGRESS"), and the second version's attempt to also scale letterSpacing/
            // paddingRight down made it worse (clipped the word "IN" outright — editing a
            // Label's own paddingRight while it's mid-layout isn't safe to do by hand). Rather
            // than keep guessing at Unity's internal text-layout formula, this measures the
            // two labels' ACTUAL on-screen bounds after each layout pass and iteratively
            // shrinks the title's font size (never touching letterSpacing/padding at all)
            // until they stop overlapping, bounded to a few passes so a persistent overlap
            // just gives up rather than spinning.
            private static void Postfix(VisualElement __result)
            {
                if (!File.Exists(ChapterJsonPath))
                {
                    return;
                }
                Label tag = __result.Q<Label>("hub-card-tag");
                VisualElement row = tag?.parent;
                if (row == null || row.childCount < 2 || !(row[0] is Label title))
                {
                    return;
                }

                row.RegisterCallback<GeometryChangedEvent>(delegate
                {
                    title.style.fontSize = TitleBaseSize;
                    FitPass(title, tag, 0);
                });
            }

            private static void FitPass(Label title, Label tag, int pass)
            {
                title.schedule.Execute((Action)delegate
                {
                    if (pass >= MaxPasses)
                    {
                        return;
                    }
                    float titleRight = title.worldBound.xMax;
                    float tagLeft = tag.worldBound.xMin;
                    float titleW = title.worldBound.width;
                    if (float.IsNaN(titleRight) || float.IsNaN(tagLeft) || float.IsNaN(titleW) || titleW <= 1f)
                    {
                        return;
                    }
                    float overlap = titleRight + MinGap - tagLeft;
                    if (overlap <= 0f)
                    {
                        return; // fits
                    }
                    float currentSize = title.resolvedStyle.fontSize;
                    float scale = Mathf.Clamp((titleW - overlap) / titleW, 0.5f, 0.98f);
                    float newSize = Mathf.Max(TitleFloorSize, Mathf.Floor(currentSize * scale));
                    if (newSize >= currentSize)
                    {
                        return; // hit the floor with no more room to give — stop rather than loop
                    }
                    title.style.fontSize = newSize;
                    FitPass(title, tag, pass + 1);
                });
            }
        }

        // --- Structure + story beat injection, run once at boot ---

        [HarmonyPatch(typeof(StructureCatalog), "get_I")]
        internal static class ChapterStructurePatch
        {
            private static bool _done;

            private static void Postfix(ref StructureCatalog __result)
            {
                if (_done || __result == null)
                {
                    return;
                }
                _done = true;

                try
                {
                    if (!File.Exists(ChapterJsonPath))
                    {
                        return;
                    }
                    JObject root = JObject.Parse(File.ReadAllText(ChapterJsonPath));
                    JObject s = (JObject)root["structure"];

                    string cellsFile = (string)s["cells_file"];
                    string cellsPath = Path.Combine(_pluginDir, "new_structures", cellsFile);
                    string cellsJson = File.Exists(cellsPath) ? File.ReadAllText(cellsPath) : null;
                    int cellCount = 0;
                    if (cellsJson != null)
                    {
                        cellCount = (int?)JObject.Parse(cellsJson)["count"] ?? 0;
                    }

                    StructureDef def = ScriptableObject.CreateInstance<StructureDef>();
                    def.id = (string)s["id"];
                    def.displayName = (string)s["display_name"];
                    def.description = (string)s["description"] ?? "";
                    def.cellCount = cellCount;
                    def.sealedLayers = (bool?)s["sealed_layers"] ?? true;
                    def.startingMoney = (int?)s["starting_money"] ?? 0;
                    def.glyphCount = (int?)s["glyph_count"] ?? 0;
                    def.greenCount = (int?)s["green_count"] ?? -1;
                    if (cellsJson != null)
                    {
                        def.cells = new TextAsset(cellsJson) { name = def.id + "_cells" };
                    }
                    if (s["bands"] is JArray bandsArr)
                    {
                        def.bands = new BandDef[bandsArr.Count];
                        for (int i = 0; i < bandsArr.Count; i++)
                        {
                            JObject b = (JObject)bandsArr[i];
                            def.bands[i] = new BandDef
                            {
                                layer = (int?)b["layer"] ?? 1,
                                hpSealed = (int?)b["hp_sealed"] ?? 75
                            };
                        }
                    }

                    int oldLen = __result.chapters?.Length ?? 0;
                    Array.Resize(ref __result.chapters, oldLen + 1);
                    __result.chapters[oldLen] = def;

                    Plugin.Log.LogInfo($"NewCampaignChapter: injected '{def.id}' into StructureCatalog.chapters (now {__result.chapters.Length} entries).");
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"NewCampaignChapter: structure injection failed: {e}");
                }
            }
        }

        // StoryScript.Load() is itself JSON-driven (reads Resources/story/campaign_story.json,
        // or campaign_story_oneship.json for the OneShip track) — it was already data-driven
        // before today's update, today's change just made it track-aware. Rather than mirror
        // its internal parsing (which already drifted once between yesterday and today — the
        // exact failure mode the project's "don't reimplement big/evolving methods" lesson
        // warns about), this lets the real Load() run to completion and splices our one extra
        // beat into its result afterward. That means future changes to the official JSON
        // schema (new fields, the "skip" flag added today, localisation key changes, etc.)
        // never need to be mirrored here at all.
        [HarmonyPatch(typeof(StoryScript), "Load")]
        internal static class StoryLoadPatch
        {
            private static void Postfix(ref StoryScript.StoryScreen[][] __result)
            {
                if (CampaignState.Track != CampaignTrack.Classic)
                {
                    return;
                }
                if (!File.Exists(ChapterJsonPath))
                {
                    return;
                }
                try
                {
                    JObject chapterRoot = JObject.Parse(File.ReadAllText(ChapterJsonPath));
                    StoryScript.StoryScreen[] newBeat = BuildModBeat((JObject)chapterRoot["story"]);

                    int insertAt = Math.Min(5, __result.Length); // after the 5 original chapter-intro beats, before ending/demo
                    var combined = new StoryScript.StoryScreen[__result.Length + 1][];
                    for (int i = 0; i < insertAt; i++)
                    {
                        combined[i] = __result[i];
                    }
                    combined[insertAt] = newBeat;
                    for (int i = insertAt; i < __result.Length; i++)
                    {
                        combined[i + 1] = __result[i];
                    }
                    __result = combined;
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"NewCampaignChapter: StoryScript.Load splice failed: {e}");
                }
            }

            private static StoryScript.StoryScreen[] BuildModBeat(JObject story)
            {
                JArray screensArr = (JArray)story["screens"];
                var screens = new StoryScript.StoryScreen[screensArr.Count];
                for (int j = 0; j < screensArr.Count; j++)
                {
                    JToken jToken = screensArr[j];
                    ColorUtility.TryParseHtmlString(((string)jToken["ink"]) ?? "#ffffff", out Color color);
                    JArray linesArr = (JArray)jToken["lines"];
                    var screen = new StoryScript.StoryScreen
                    {
                        tag = (string)jToken["tag"] ?? "",
                        art = (string)jToken["art"] ?? "",
                        next = (string)jToken["next"] ?? "NEXT",
                        ink = color,
                        wreck = (string)jToken["wreck"],
                        lines = new StoryScript.StoryLine[linesArr.Count]
                    };
                    for (int k = 0; k < linesArr.Count; k++)
                    {
                        JArray partsArr = (JArray)linesArr[k]["parts"];
                        var line = new StoryScript.StoryLine
                        {
                            kind = (string)linesArr[k]["kind"] ?? "say",
                            parts = new StoryScript.Part[partsArr.Count]
                        };
                        for (int l = 0; l < partsArr.Count; l++)
                        {
                            line.parts[l] = new StoryScript.Part { text = (string)partsArr[l]["text"] };
                        }
                        screen.lines[k] = line;
                    }
                    screens[j] = screen;
                }
                return screens;
            }
        }

        // Play(int)/ArtBeat(int) bodies are byte-identical to yesterday's decompile — unaffected
        // by today's update — so the only fix needed is the one this session never actually
        // added: guard on Classic track, since both previously ran unconditionally and would
        // have hijacked OneShip's demo/ending lookups too.
        [HarmonyPatch(typeof(StoryScript), "Play")]
        internal static class StoryPlayPatch
        {
            private static bool Prefix(int code, ref StoryScript.StoryScreen[] __result)
            {
                if (CampaignState.Track != CampaignTrack.Classic || !File.Exists(ChapterJsonPath))
                {
                    return true;
                }

                StoryScript.StoryScreen[] demoBeat = StoryScript.Beat(7); // was Beat(6) — shifted +1 by our insertion
                switch (code)
                {
                    case 100:
                    {
                        var array2 = (StoryScript.StoryScreen[])StoryScript.Beat(0).Clone();
                        int idx = Array.FindIndex(array2, s => s.art == "scan");
                        if (idx >= 0 && demoBeat.Length != 0)
                        {
                            array2[idx] = demoBeat[0];
                        }
                        __result = array2;
                        return false;
                    }
                    case 101:
                        __result = (demoBeat.Length <= 1) ? new StoryScript.StoryScreen[0] : new[] { demoBeat[1] };
                        return false;
                    default:
                        __result = StoryScript.Beat(code);
                        return false;
                }
            }
        }

        [HarmonyPatch(typeof(StoryScript), "ArtBeat")]
        internal static class ArtBeatPatch
        {
            private static bool Prefix(int code, ref int __result)
            {
                if (CampaignState.Track != CampaignTrack.Classic || !File.Exists(ChapterJsonPath))
                {
                    return true;
                }
                __result = code switch { 101 => 6, 100 => 0, _ => code }; // EndingBeat shifted 5->6
                return false;
            }
        }

        // Newly found this recheck: ResultsBoot.Continue() hardcodes IntroBoot.StoryBeat = 5
        // to trigger the "you finished the campaign" ending sequence. Unchanged since
        // yesterday — this was a latent gap in last session's work too (the end-to-end
        // "finish chapter 6" path was never actually play-tested), not something today's
        // patch introduced. Letting the original method run and correcting its one
        // documented side effect afterward avoids ever reimplementing its finished/demo
        // branching logic.
        [HarmonyPatch(typeof(ResultsBoot), "Continue")]
        internal static class ResultsEndingBeatPatch
        {
            private static void Postfix()
            {
                if (CampaignState.Track == CampaignTrack.Classic && IntroBoot.StoryBeat == 5)
                {
                    IntroBoot.StoryBeat = 6;
                }
            }
        }
    }
}
