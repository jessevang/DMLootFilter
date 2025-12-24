using System;
using System.Collections.Generic;
using UnityEngine;

namespace DMLootFilter
{
    internal static class LootFilterSnapshotTracker
    {
        private sealed class Pending
        {
            public Dictionary<Key, Entry> Before;
        }

        private struct Key : IEquatable<Key>
        {
            public int Type;
            public int Quality;

            public bool Equals(Key other) => Type == other.Type && Quality == other.Quality;
            public override bool Equals(object obj) => obj is Key other && Equals(other);
            public override int GetHashCode()
            {
                unchecked
                {
                    return (Type * 397) ^ Quality;
                }
            }
        }

        private sealed class Entry
        {
            public ItemValue ItemValue;
            public int Count;
        }

        private static readonly Dictionary<string, Pending> _pendingByPlayerId =
            new Dictionary<string, Pending>(StringComparer.OrdinalIgnoreCase);

        public static void TrySpawnPendingDrops(string playerId, ClientInfo cInfo, EntityPlayer livePlayer, HashSet<string> filterLower)
        {
            if (string.IsNullOrWhiteSpace(playerId)) return;
            if (cInfo == null || cInfo.latestPlayerData == null) return;
            if (livePlayer == null || !livePlayer.IsSpawned() || livePlayer.IsDead()) return;
            if (filterLower == null || filterLower.Count == 0) return;

            if (!_pendingByPlayerId.TryGetValue(playerId, out var pending) || pending?.Before == null)
                return;

            var after = BuildSnapshot(cInfo, filterLower);
            if (after == null)
            {
                _pendingByPlayerId.Remove(playerId);
                return;
            }

            foreach (var kvp in pending.Before)
            {
                var key = kvp.Key;
                var beforeEntry = kvp.Value;

                int afterCount = 0;
                if (after.TryGetValue(key, out var afterEntry))
                    afterCount = afterEntry.Count;

                int removed = beforeEntry.Count - afterCount;
                if (removed <= 0)
                    continue;

                var iv = beforeEntry.ItemValue;
                if (iv == null || iv.IsEmpty())
                    continue;

                var stack = new ItemStack(iv.Clone(), removed);
                LootFilterDropper.DropNearPlayer(livePlayer, stack, removed);
            }

            _pendingByPlayerId.Remove(playerId);
        }

        public static void SetPendingBeforeSnapshot(string playerId, ClientInfo cInfo, HashSet<string> filterLower)
        {
            if (string.IsNullOrWhiteSpace(playerId)) return;
            if (cInfo == null || cInfo.latestPlayerData == null) return;
            if (filterLower == null || filterLower.Count == 0) return;

            var before = BuildSnapshot(cInfo, filterLower);
            if (before == null) return;

            _pendingByPlayerId[playerId] = new Pending { Before = before };
        }

        private static Dictionary<Key, Entry> BuildSnapshot(ClientInfo cInfo, HashSet<string> filterLower)
        {
            var pdf = cInfo?.latestPlayerData;
            if (pdf == null) return null;

            var snapshot = new Dictionary<Key, Entry>();

            AddStacks(snapshot, pdf.inventory, filterLower);
            AddStacks(snapshot, pdf.bag, filterLower);

            try
            {
                var eq = pdf.equipment;
                if (eq != null)
                {
                    int slotCount = eq.GetSlotCount();
                    for (int i = 0; i < slotCount; i++)
                    {
                        var iv = eq.GetSlotItem(i);
                        if (iv == null || iv.IsEmpty()) continue;

                        string name = iv.ItemClass?.Name ?? iv.ItemClass?.GetItemName();
                        if (string.IsNullOrWhiteSpace(name)) continue;

                        if (!filterLower.Contains(name.Trim().ToLowerInvariant()))
                            continue;

                        int type = iv.type;
                        int quality = GetQuality(iv);

                        var key = new Key { Type = type, Quality = quality };

                        if (!snapshot.TryGetValue(key, out var entry))
                        {
                            entry = new Entry { ItemValue = iv.Clone(), Count = 0 };
                            snapshot[key] = entry;
                        }

                        entry.Count += 1;
                    }
                }
            }
            catch { }

            return snapshot;
        }

        private static void AddStacks(Dictionary<Key, Entry> snapshot, ItemStack[] stacks, HashSet<string> filterLower)
        {
            if (stacks == null) return;

            for (int i = 0; i < stacks.Length; i++)
            {
                var st = stacks[i];
                if (st == null || st.IsEmpty()) continue;

                var iv = st.itemValue;
                if (iv == null || iv.IsEmpty()) continue;

                string name = iv.ItemClass?.Name ?? iv.ItemClass?.GetItemName();
                if (string.IsNullOrWhiteSpace(name)) continue;

                if (!filterLower.Contains(name.Trim().ToLowerInvariant()))
                    continue;

                int type = iv.type;
                int quality = GetQuality(iv);

                var key = new Key { Type = type, Quality = quality };

                if (!snapshot.TryGetValue(key, out var entry))
                {
                    entry = new Entry { ItemValue = iv.Clone(), Count = 0 };
                    snapshot[key] = entry;
                }

                entry.Count += Math.Max(0, st.count);
            }
        }

        private static int GetQuality(ItemValue iv)
        {
            if (iv == null) return 0;

            try { return iv.Quality; } catch { }

            try
            {
                var t = iv.GetType();
                var p = t.GetProperty("quality");
                if (p != null)
                {
                    object v = p.GetValue(iv, null);
                    if (v is int qi) return qi;
                }
            }
            catch { }

            return 0;
        }
    }
}
