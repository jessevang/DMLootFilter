using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace DMLootFilter
{
    internal static class LootFilterUtil
    {
        public const string FilterBoxName = "filter";

        public static string GetContainerCustomNameOrEmpty(TileEntity te)
        {
            try
            {
                if (te == null) return "";

                if (te.TryGetSelfOrFeature<ITileEntitySignable>(out var signable) && signable != null)
                {
                    var authored = signable.GetAuthoredText();
                    if (authored != null && !string.IsNullOrWhiteSpace(authored.Text))
                        return authored.Text.Trim();
                }
            }
            catch { }

            return "";
        }

        public static ClientInfo TryGetClientInfoByEntityId(int entityId)
        {
            return ClientInfoUtil.TryGetClientInfoByEntityId(entityId);
        }

        public static int TryGetEntityIdAccessing(TileEntity te)
        {
            try
            {
                var gm = GameManager.Instance;
                if (gm == null || te == null) return -1;
                return gm.GetEntityIDForLockedTileEntity(te);
            }
            catch { }

            return -1;
        }

        public static void SnapshotFilterFromLootable(ITileEntityLootable lootable, string playerId, bool saveNow = true)
        {
            if (lootable == null) return;
            if (string.IsNullOrWhiteSpace(playerId)) return;

            try
            {
                var items = TryGetItemsArray(lootable);
                if (items == null)
                {
                    Debug.LogWarning($"[DMLootFilter] SnapshotFilterFromLootable: Could not read items array from lootable type={lootable.GetType().FullName}");
                    return;
                }

                var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                for (int i = 0; i < items.Length; i++)
                {
                    if (items[i].IsEmpty()) continue;

                    int typeId;
                    try { typeId = items[i].itemValue.type; }
                    catch { continue; }

                    if (typeId < 0) continue;

                    string key = "";
                    try
                    {
                        var ic = ItemClass.GetForId(typeId);
                        if (ic != null) key = ic.Name ?? "";
                    }
                    catch { }

                    if (string.IsNullOrWhiteSpace(key)) continue;

                    set.Add(key);
                }

                PlayerDataStore.PlayerStorage.SetLootFilterNames(playerId, set);

                Debug.Log($"[DMLootFilter] Updated filter from lootable for {playerId}: {set.Count} item keys.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DMLootFilter] SnapshotFilterFromLootable failed: {ex}");
            }
        }

        private static ItemStack[] TryGetItemsArray(ITileEntityLootable lootable)
        {
            if (lootable == null) return null;

            if (lootable is TileEntityLootContainer lc)
                return lc.items;

            Type t = lootable.GetType();

            try
            {
                var mi = AccessTools.Method(t, "GetItems");
                if (mi != null)
                {
                    var val = mi.Invoke(lootable, null);
                    var arr = val as ItemStack[];
                    if (arr != null) return arr;
                }
            }
            catch { }

            try
            {
                var pi = AccessTools.Property(t, "Items");
                if (pi != null)
                {
                    var val = pi.GetValue(lootable, null);
                    var arr = val as ItemStack[];
                    if (arr != null) return arr;
                }
            }
            catch { }

            try
            {
                var pi = AccessTools.Property(t, "items");
                if (pi != null)
                {
                    var val = pi.GetValue(lootable, null);
                    var arr = val as ItemStack[];
                    if (arr != null) return arr;
                }
            }
            catch { }

            try
            {
                var fi = AccessTools.Field(t, "itemsArr")
                         ?? AccessTools.Field(t, "items")
                         ?? AccessTools.Field(t, "Items")
                         ?? AccessTools.Field(t, "_items")
                         ?? AccessTools.Field(t, "m_Items")
                         ?? AccessTools.Field(t, "itemStacks")
                         ?? AccessTools.Field(t, "_itemStacks")
                         ?? AccessTools.Field(t, "m_itemStacks");

                if (fi != null)
                {
                    var val = fi.GetValue(lootable);
                    var arr = val as ItemStack[];
                    if (arr != null) return arr;
                }
            }
            catch { }

            return null;
        }

        public static bool IsNamedFilterBox(TileEntityLootContainer lc)
        {
            if (lc == null) return false;
            if (!lc.bPlayerStorage) return false;

            string name = GetContainerCustomNameOrEmpty(lc);
            if (string.IsNullOrWhiteSpace(name)) return false;

            return name.Equals(FilterBoxName, StringComparison.OrdinalIgnoreCase);
        }
    }
}
