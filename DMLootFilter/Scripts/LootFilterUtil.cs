using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace DMLootFilter
{
    internal static class LootFilterUtil
    {
        public const string FilterBoxName = "filter";

        // Cache "how to get items" per lootable type (silent + fast)
        private static readonly Dictionary<Type, Func<object, ItemStack[]>> _itemsGetterByType =
            new Dictionary<Type, Func<object, ItemStack[]>>();

        private static readonly object _itemsGetterLock = new object();

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

            // Fast path
            if (lootable is TileEntityLootContainer lc)
                return lc.items;

            var t = lootable.GetType();
            Func<object, ItemStack[]> getter;

            lock (_itemsGetterLock)
            {
                if (!_itemsGetterByType.TryGetValue(t, out getter))
                {
                    getter = BuildItemsGetter(t);
                    // cache even if null (so we don't re-probe every time)
                    _itemsGetterByType[t] = getter;
                }
            }

            if (getter == null)
                return null;

            try
            {
                return getter(lootable);
            }
            catch
            {
                return null;
            }
        }

        private static Func<object, ItemStack[]> BuildItemsGetter(Type t)
        {
            // NOTE: We intentionally do NOT use Harmony AccessTools here,
            // because AccessTools logs warnings on misses.
            const BindingFlags BF = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            // 1) Method: GetItems() : ItemStack[]
            try
            {
                var mi = t.GetMethod("GetItems", BF, null, Type.EmptyTypes, null);
                if (mi != null)
                {
                    return (obj) => ConvertToItemStackArray(mi.Invoke(obj, null));
                }
            }
            catch { }

            // 2) Property: Items / items
            try
            {
                var pi = t.GetProperty("Items", BF) ?? t.GetProperty("items", BF);
                if (pi != null)
                {
                    return (obj) => ConvertToItemStackArray(pi.GetValue(obj, null));
                }
            }
            catch { }

            // 3) Field: common names
            string[] fieldNames =
            {
                "itemsArr",
                "items",
                "Items",
                "_items",
                "m_Items",
                "itemStacks",
                "_itemStacks",
                "m_itemStacks"
            };

            foreach (var fn in fieldNames)
            {
                try
                {
                    var fi = t.GetField(fn, BF);
                    if (fi != null)
                    {
                        return (obj) => ConvertToItemStackArray(fi.GetValue(obj));
                    }
                }
                catch { }
            }

            // Nothing found for this type
            return null;
        }

        private static ItemStack[] ConvertToItemStackArray(object val)
        {
            if (val == null) return null;

            if (val is ItemStack[] arr)
                return arr;

            // Some containers might store as List<ItemStack> or IList<ItemStack>
            if (val is IList<ItemStack> list)
            {
                var a = new ItemStack[list.Count];
                for (int i = 0; i < list.Count; i++)
                    a[i] = list[i];
                return a;
            }

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
