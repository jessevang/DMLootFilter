using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace DMLootFilter
{
    internal static class LootFilterXPathGenerator
    {
        // Where 7DTD will actually load xpath patches from for this mod
        private static string ConfigDir => GameIO.GetGameDir("Mods/DMLootFilter/Config/");
        private static string ItemsXPathPath => Path.Combine(ConfigDir, "items.xml");
        private static string EventsXPathPath => Path.Combine(ConfigDir, "gameevents.xml");

        private const string ActionPrefix = "remove_";

        // throttle regen (don’t rebuild files 50 times per second)
        private static float _nextAllowedTime;
        private static bool _dirty;

        public static void MarkDirty()
        {
            _dirty = true;
        }

        public static void Tick()
        {
            if (!_dirty) return;

            if (Time.time < _nextAllowedTime)
                return;

            _nextAllowedTime = Time.time + 2.0f; // regen at most every 2 seconds
            _dirty = false;

            try
            {
                GenerateFromAllPlayers();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DMLootFilter] XPath generation failed: {ex}");
            }
        }

        public static void GenerateFromAllPlayers()
        {
            Directory.CreateDirectory(ConfigDir);

            // Collect unique item names across all players’ filter lists
            HashSet<string> all = PlayerDataStore_PlayerStorageReflection.GetAllFilteredItemNames();
            if (all == null) all = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Remove invalid item names (and optionally validate they exist)
            var valid = new List<string>();
            foreach (var name in all)
            {
                var n = NormalizeItemName(name);
                if (string.IsNullOrWhiteSpace(n)) continue;

                // Validate item exists in current game
                // (prevents writing xpath entries for junk strings)
                try
                {
                    var iv = ItemClass.GetItem(n, false);
                    if (iv == null || iv.type == ItemValue.None.type) continue;
                }
                catch { continue; }

                valid.Add(n);
            }

            valid.Sort(StringComparer.OrdinalIgnoreCase);

            WriteItemsXml(valid);
            WriteGameEventsXml(valid);

            Debug.Log($"[DMLootFilter] Generated xpath: items={valid.Count}, events={valid.Count}");
        }

        private static void WriteItemsXml(List<string> itemNames)
        {
            using (var sw = new StreamWriter(ItemsXPathPath, false, Encoding.UTF8))
            {
                sw.WriteLine("<configs>");
                sw.WriteLine("  <!-- DMLootFilter auto-generated. DO NOT EDIT. -->");
                sw.WriteLine();

                foreach (var itemName in itemNames)
                {
                    // Add tag ,<itemName> to that item
                    // Note: if item has no Tags property, this xpath may fail; usually items have Tags.
                    sw.WriteLine(
                        "  <append xpath=\"/items/item[@name='{0}']/property[@name='Tags']/@value\">,{1}</append>",
                        EscapeAttr(itemName),
                        EscapeText(itemName)
                    );
                }

                sw.WriteLine();
                sw.WriteLine("</configs>");
            }
        }

        private static void WriteGameEventsXml(List<string> itemNames)
        {
            using (var sw = new StreamWriter(EventsXPathPath, false, Encoding.UTF8))
            {
                sw.WriteLine("<configs>");
                sw.WriteLine("  <!-- DMLootFilter auto-generated. DO NOT EDIT. -->");
                sw.WriteLine();
                sw.WriteLine("  <append xpath=\"/gameevents\">");
                sw.WriteLine();

                foreach (var itemName in itemNames)
                {
                    string actionName = ActionPrefix + SanitizeForActionName(itemName);

                    sw.WriteLine("    <action_sequence name=\"{0}\">", EscapeAttr(actionName));
                    sw.WriteLine("      <action class=\"RemoveItems\">");
                    sw.WriteLine("        <property name=\"items_location\" value=\"Toolbelt,Backpack\" />");
                    sw.WriteLine("        <property name=\"items_tags\" value=\"{0}\" />", EscapeAttr(itemName));
                    sw.WriteLine("      </action>");
                    sw.WriteLine("    </action_sequence>");
                    sw.WriteLine();
                }

                sw.WriteLine("  </append>");
                sw.WriteLine();
                sw.WriteLine("</configs>");
            }
        }

        private static string NormalizeItemName(string s)
        {
            return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        }

        private static string SanitizeForActionName(string raw)
        {
            // Keep this aligned with how your watcher builds action names.
            // We keep original casing but replace unsafe characters.
            var sb = new StringBuilder(raw.Length);
            foreach (char ch in raw)
            {
                if ((ch >= 'a' && ch <= 'z') ||
                    (ch >= 'A' && ch <= 'Z') ||
                    (ch >= '0' && ch <= '9') ||
                    ch == '_' || ch == '-')
                    sb.Append(ch);
                else
                    sb.Append('_');
            }
            return sb.ToString();
        }

        private static string EscapeAttr(string s)
        {
            // minimal XML attribute escaping
            return s.Replace("&", "&amp;").Replace("'", "&apos;").Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;");
        }

        private static string EscapeText(string s)
        {
            // minimal text escaping
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }
    }

    /// <summary>
    /// Your PlayerDataStore.PlayerStorage has the data dictionary locked private.
    /// Easiest: add a proper API to PlayerStorage to enumerate all keys.
    /// Since you pasted PlayerDataStore, I’m giving you a minimal approach:
    /// add a method to PlayerStorage called GetAllFilterNamesSnapshot().
    /// If you don’t want to modify internals, we can do reflection — but API is better.
    /// </summary>
    internal static class PlayerDataStore_PlayerStorageReflection
    {
        public static HashSet<string> GetAllFilteredItemNames()
        {
            // BEST PRACTICE: implement PlayerStorage.GetAllFilterNamesSnapshot() instead.
            // For now, call that if you add it.
            return PlayerDataStore.PlayerStorage.GetAllFilterNamesSnapshot();
        }
    }
}
