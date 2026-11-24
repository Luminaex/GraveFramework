using System;
using System.Collections.Generic;
using System.Reflection;
using GraveyardOrbit;
using HarmonyLib;

namespace GraveFrameworkExamples
{
    // Demonstrates a mod documenting its own content in the in-game Handbook.
    // HandbookBook.Entry/Section/Block/Card are public nested classes, but the loaded instance
    // (private static Book _book) isn't exposed directly — same shape as every other catalog
    // this project has extended (ModCatalog._all, AnomalyCatalog.Implemented, etc.): reflect
    // into the private backing field once the real load has happened via HandbookBook.Reload(),
    // and add to it. Reload() re-runs on every language change (Loc.Changed clears _book), so
    // this postfix guards against re-adding the same ids twice.
    //
    // NOT yet verified live in-game (built but not smoke-tested) — unlike every other technique
    // in this project, which was confirmed working before being written up. Check this one
    // in-game (open the Handbook, look for a "MODDING" section) before relying on it.
    [HarmonyPatch(typeof(HandbookBook), nameof(HandbookBook.Reload))]
    internal static class HandbookInjectPatch
    {
        private const string SectionId = "framework_examples";
        private const string EntryId = "framework_examples_entry";

        private static readonly FieldInfo BookField = AccessTools.Field(typeof(HandbookBook), "_book");

        private static void Postfix()
        {
            try
            {
                object book = BookField.GetValue(null);
                if (book == null)
                {
                    return;
                }

                Type bookType = book.GetType();
                var sections = (List<HandbookBook.Section>)AccessTools.Field(bookType, "sections").GetValue(book);
                var entries = (List<HandbookBook.Entry>)AccessTools.Field(bookType, "entries").GetValue(book);

                if (sections.Find(s => s.id == SectionId) == null)
                {
                    sections.Add(new HandbookBook.Section
                    {
                        id = SectionId,
                        title = "MODDING",
                        color = "#9de8ff"
                    });
                }

                if (entries.Find(e => e.id == EntryId) == null)
                {
                    entries.Add(new HandbookBook.Entry
                    {
                        id = EntryId,
                        section = SectionId,
                        title = "Modding This Game",
                        lead = "This entry was added entirely by a mod, through GraveFramework.",
                        blocks = new List<HandbookBook.Block>
                        {
                            new HandbookBook.Block
                            {
                                type = "text",
                                text = "GraveFrameworkExamples demonstrates every technique GraveFramework and the base game support: new campaign chapters, ship parts, weapon mechanics, localisation, and — this entry — a mod documenting its own content in the Handbook itself."
                            }
                        }
                    });
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Handbook inject failed: {e}");
            }
        }
    }
}
