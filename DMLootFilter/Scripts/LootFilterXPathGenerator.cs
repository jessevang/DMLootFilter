using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace DMLootFilter
{
    internal static class LootFilterXPathGenerator
    {
        private static string ConfigDir => GameIO.GetGameDir("Mods/DMLootFilter/Config/");
        private static string ItemsXPathPath => Path.Combine(ConfigDir, "items.xml");
        private static string EventsXPathPath => Path.Combine(ConfigDir, "gameevents.xml");

        private const string ActionPrefix = "remove_";

        private static float _nextAllowedTime;
        private static bool _dirty;

        private static float _lastCriticalLogTime = -9999f;
        private const float CriticalLogThrottleSeconds = 30f;

        private static void LogCriticalThrottled(string msg)
        {
            float now = Time.time;
            if (now - _lastCriticalLogTime < CriticalLogThrottleSeconds)
                return;

            _lastCriticalLogTime = now;
            Debug.LogError("[DMLootFilter] CRITICAL: " + msg);
        }

        public static void MarkDirty()
        {
            _dirty = true;
        }

        public static void Tick()
        {
            if (!_dirty) return;
            if (Time.time < _nextAllowedTime) return;

            _nextAllowedTime = Time.time + 2.0f;
            _dirty = false;

            try
            {
                GenerateFromAllPlayers();
            }
            catch (Exception ex)
            {
                LogCriticalThrottled("XPath generation failed ex=" + ex);
            }
        }

        public static void GenerateFromAllPlayers()
        {
            Directory.CreateDirectory(ConfigDir);

            // NEW: this now unions ALL boxes for ALL players
            HashSet<string> all = PlayerDataStore_PlayerStorageReflection.GetAllFilteredItemNames();
            if (all == null)
                all = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var valid = new List<string>();

            foreach (var name in all)
            {
                var n = NormalizeItemName(name);
                if (string.IsNullOrWhiteSpace(n)) continue;

                try
                {
                    var iv = ItemClass.GetItem(n, false);
                    if (iv == null || iv.type == ItemValue.None.type) continue;
                }
                catch
                {
                    continue;
                }

                valid.Add(n);
            }

            valid.Sort(StringComparer.OrdinalIgnoreCase);

            WriteItemsXml(valid);
            WriteGameEventsXml(valid);
        }

        private static void WriteItemsXml(List<string> itemNames)
        {
            using (var sw = new StreamWriter(ItemsXPathPath, false, Encoding.UTF8))
            {
                sw.WriteLine("<configs>");
                sw.WriteLine();

                foreach (var itemName in itemNames)
                {
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
            return s.Replace("&", "&amp;")
                    .Replace("'", "&apos;")
                    .Replace("\"", "&quot;")
                    .Replace("<", "&lt;")
                    .Replace(">", "&gt;");
        }

        private static string EscapeText(string s)
        {
            return s.Replace("&", "&amp;")
                    .Replace("<", "&lt;")
                    .Replace(">", "&gt;");
        }
    }

    internal static class PlayerDataStore_PlayerStorageReflection
    {
        public static HashSet<string> GetAllFilteredItemNames()
        {
            return PlayerDataStore.PlayerStorage.GetAllFilterNamesSnapshot();
        }
    }
}