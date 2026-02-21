using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace DMLootFilter
{
    internal static class PlayerDataStore
    {
        internal static class LootFilterKeys
        {
            public const string BaseName = "filter";
            public const int MaxIndex = 10; // supports filter1..filter10

            public static string Normalize(string key)
            {
                if (string.IsNullOrWhiteSpace(key)) return null;
                return key.Trim().ToLowerInvariant();
            }

            public static bool IsValid(string key)
            {
                key = Normalize(key);
                if (string.IsNullOrWhiteSpace(key)) return false;

                if (key.Equals(BaseName, StringComparison.OrdinalIgnoreCase))
                    return true;

                if (!key.StartsWith(BaseName, StringComparison.OrdinalIgnoreCase))
                    return false;

                // filter1..filter10 only
                var suffix = key.Substring(BaseName.Length);
                if (suffix.Length == 0) return true;

                if (!int.TryParse(suffix, out int n)) return false;
                return n >= 1 && n <= MaxIndex;
            }
        }

        public class PlayerData
        {
            public string playerId;

            // NEW: per-box filter sets: "filter", "filter1", ... "filter10"
            [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
            public Dictionary<string, HashSet<string>> LootFilterItemNamesByBox =
                new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

            // LEGACY: pre-multi-box storage. Kept only for migration.
            [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
            public HashSet<string> LootFilterItemNames =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Runtime cache to avoid rebuilding union every second
            [JsonIgnore]
            internal HashSet<string> _cachedUnion;

            [JsonIgnore]
            internal bool _unionDirty = true;
        }

        public static class PlayerStorage
        {
            private static readonly object _lock = new object();

            private static Dictionary<string, PlayerData> data =
                new Dictionary<string, PlayerData>(StringComparer.OrdinalIgnoreCase);

            private static bool _dirty;

            private static string SavePath => GameIO.GetGameDir("Mods/DMLootFilter/Data/player_data.json");

            private static string NormalizeId(string id)
            {
                if (string.IsNullOrWhiteSpace(id))
                    return null;
                return id.Trim();
            }

            private static string NormalizeKey(string key)
            {
                if (string.IsNullOrWhiteSpace(key))
                    return null;
                return key.Trim();
            }

            private static void Ensure(PlayerData pd, string idForSafety = null)
            {
                if (pd == null) return;

                if (!string.IsNullOrWhiteSpace(idForSafety))
                    pd.playerId = idForSafety;

                if (pd.LootFilterItemNamesByBox == null)
                    pd.LootFilterItemNamesByBox = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

                // legacy set exists for migration; keep it non-null to avoid null refs
                if (pd.LootFilterItemNames == null)
                    pd.LootFilterItemNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (pd._cachedUnion == null)
                    pd._cachedUnion = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                pd._unionDirty = true;
            }

            private static void MarkUnionDirty(PlayerData pd)
            {
                if (pd == null) return;
                pd._unionDirty = true;
            }

            private static void EnsureDirExists(string path)
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
            }

            public static void Load()
            {
                lock (_lock)
                {
                    try
                    {
                        string path = SavePath;
                        EnsureDirExists(path);

                        if (!File.Exists(path))
                        {
                            data = new Dictionary<string, PlayerData>(StringComparer.OrdinalIgnoreCase);
                            _dirty = true;
                            Save_NoLock();
                            return;
                        }

                        string json = File.ReadAllText(path);
                        if (string.IsNullOrWhiteSpace(json))
                        {
                            data = new Dictionary<string, PlayerData>(StringComparer.OrdinalIgnoreCase);
                            _dirty = true;
                            Save_NoLock();
                            return;
                        }

                        var loaded = JsonConvert.DeserializeObject<Dictionary<string, PlayerData>>(json);
                        data = loaded ?? new Dictionary<string, PlayerData>(StringComparer.OrdinalIgnoreCase);

                        var cleaned = new Dictionary<string, PlayerData>(StringComparer.OrdinalIgnoreCase);

                        foreach (var kv in data)
                        {
                            string key = NormalizeId(kv.Key);
                            if (string.IsNullOrWhiteSpace(key))
                                continue;

                            var pd = kv.Value ?? new PlayerData();
                            Ensure(pd, key);

                            // --- Normalize / clean per-box sets ---
                            var normalizedByBox = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

                            if (pd.LootFilterItemNamesByBox != null)
                            {
                                foreach (var bkv in pd.LootFilterItemNamesByBox)
                                {
                                    var boxKey = LootFilterKeys.Normalize(bkv.Key);
                                    if (!LootFilterKeys.IsValid(boxKey))
                                        continue;

                                    var srcSet = bkv.Value;
                                    var dstSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                                    if (srcSet != null)
                                    {
                                        foreach (var n in srcSet)
                                        {
                                            var nn = NormalizeKey(n);
                                            if (string.IsNullOrWhiteSpace(nn)) continue;
                                            dstSet.Add(nn);
                                        }
                                    }

                                    normalizedByBox[boxKey] = dstSet;
                                }
                            }

                            pd.LootFilterItemNamesByBox = normalizedByBox;

                            // --- Migration: if old LootFilterItemNames existed, move into "filter" ---
                            if (pd.LootFilterItemNames != null && pd.LootFilterItemNames.Count > 0)
                            {
                                if (!pd.LootFilterItemNamesByBox.TryGetValue(LootFilterKeys.BaseName, out var baseSet) || baseSet == null)
                                {
                                    baseSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                                    pd.LootFilterItemNamesByBox[LootFilterKeys.BaseName] = baseSet;
                                }

                                foreach (var n in pd.LootFilterItemNames)
                                {
                                    var nn = NormalizeKey(n);
                                    if (string.IsNullOrWhiteSpace(nn)) continue;
                                    baseSet.Add(nn);
                                }

                                // clear legacy to stop re-saving it
                                pd.LootFilterItemNames.Clear();
                                _dirty = true;
                            }

                            MarkUnionDirty(pd);

                            cleaned[key] = pd;
                        }

                        data = cleaned;
                        _dirty = false;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[DMLootFilter] PlayerStorage.Load FAILED. Path='{SavePath}'. Error: {ex}");
                        data = new Dictionary<string, PlayerData>(StringComparer.OrdinalIgnoreCase);
                        _dirty = false;
                    }
                }



            }

            public static bool HasFilterName(string playerId, string itemNameKey)
            {
                if (string.IsNullOrWhiteSpace(playerId))
                    return false;

                if (string.IsNullOrWhiteSpace(itemNameKey))
                    return false;

                var union = GetLootFilterNamesUnion(playerId);
                if (union == null || union.Count == 0)
                    return false;

                return union.Contains(itemNameKey.Trim());
            }

            public static void Save()
            {
                lock (_lock)
                {
                    Save_NoLock();
                }
            }

            private static void Save_NoLock()
            {
                string path = SavePath;

                try
                {
                    if (!_dirty && data.Count > 0)
                        return;

                    EnsureDirExists(path);

                    var normalized = new Dictionary<string, PlayerData>(StringComparer.OrdinalIgnoreCase);

                    foreach (var kv in data)
                    {
                        string key = NormalizeId(kv.Key);
                        if (string.IsNullOrWhiteSpace(key))
                            continue;

                        var pd = kv.Value ?? new PlayerData();
                        Ensure(pd, key);

                        // Normalize per-box dictionary
                        var normByBox = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

                        foreach (var bkv in pd.LootFilterItemNamesByBox ?? new Dictionary<string, HashSet<string>>())
                        {
                            var boxKey = LootFilterKeys.Normalize(bkv.Key);
                            if (!LootFilterKeys.IsValid(boxKey))
                                continue;

                            var dst = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            var src = bkv.Value;

                            if (src != null)
                            {
                                foreach (var n in src)
                                {
                                    var nn = NormalizeKey(n);
                                    if (string.IsNullOrWhiteSpace(nn)) continue;
                                    dst.Add(nn);
                                }
                            }

                            normByBox[boxKey] = dst;
                        }

                        pd.LootFilterItemNamesByBox = normByBox;

                        // Do not save legacy list anymore
                        if (pd.LootFilterItemNames != null && pd.LootFilterItemNames.Count > 0)
                            pd.LootFilterItemNames.Clear();

                        normalized[key] = pd;
                    }

                    data = normalized;

                    string json = JsonConvert.SerializeObject(data, Formatting.Indented);

                    string tmp = path + ".tmp";
                    File.WriteAllText(tmp, json);

                    if (File.Exists(path))
                    {
                        string bak = path + ".bak";
                        try
                        {
                            File.Replace(tmp, path, bak, ignoreMetadataErrors: true);
                        }
                        catch
                        {
                            try { File.Delete(path); } catch { }
                            File.Move(tmp, path);
                        }
                    }
                    else
                    {
                        File.Move(tmp, path);
                    }

                    _dirty = false;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[DMLootFilter] PlayerStorage.Save FAILED. Path='{path}'. Error: {ex}");
                }
            }

            public static PlayerData Get(string playerId, bool saveIfNew = false)
            {
                lock (_lock)
                {
                    playerId = NormalizeId(playerId);
                    if (string.IsNullOrWhiteSpace(playerId))
                        throw new Exception("[DMLootFilter] PlayerStorage.Get called with empty playerId.");

                    bool created = false;

                    if (!data.TryGetValue(playerId, out var pd) || pd == null)
                    {
                        pd = new PlayerData();
                        Ensure(pd, playerId);
                        data[playerId] = pd;

                        created = true;
                        _dirty = true;
                    }
                    else
                    {
                        Ensure(pd, playerId);
                    }

                    if (created && saveIfNew)
                        Save_NoLock();

                    return pd;
                }
            }

            // --- API: per-filter-box get/set ---
            public static HashSet<string> GetLootFilterNames(string playerId, string filterKey = "filter")
            {
                lock (_lock)
                {
                    var pd = Get(playerId);
                    filterKey = LootFilterKeys.Normalize(filterKey) ?? LootFilterKeys.BaseName;

                    if (!LootFilterKeys.IsValid(filterKey))
                        filterKey = LootFilterKeys.BaseName;

                    if (!pd.LootFilterItemNamesByBox.TryGetValue(filterKey, out var set) || set == null)
                    {
                        set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        pd.LootFilterItemNamesByBox[filterKey] = set;
                        _dirty = true;
                        MarkUnionDirty(pd);
                    }

                    return set;
                }
            }

            public static void SetLootFilterNames(string playerId, string filterKey, IEnumerable<string> itemNameKeys, bool saveNow = true)
            {
                lock (_lock)
                {
                    var pd = Get(playerId);

                    filterKey = LootFilterKeys.Normalize(filterKey) ?? LootFilterKeys.BaseName;
                    if (!LootFilterKeys.IsValid(filterKey))
                        filterKey = LootFilterKeys.BaseName;

                    var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    if (itemNameKeys != null)
                    {
                        foreach (var k in itemNameKeys)
                        {
                            var nk = NormalizeKey(k);
                            if (string.IsNullOrWhiteSpace(nk)) continue;
                            set.Add(nk);
                        }
                    }

                    bool changed = !pd.LootFilterItemNamesByBox.TryGetValue(filterKey, out var existing)
                                   || existing == null
                                   || !existing.SetEquals(set);

                    pd.LootFilterItemNamesByBox[filterKey] = set;

                    if (changed)
                    {
                        _dirty = true;
                        MarkUnionDirty(pd);
                        if (saveNow) Save_NoLock();
                    }
                }
            }

            public static void ClearLootFilter(string playerId, string filterKey = "filter", bool saveNow = true)
            {
                lock (_lock)
                {
                    var pd = Get(playerId);

                    filterKey = LootFilterKeys.Normalize(filterKey) ?? LootFilterKeys.BaseName;
                    if (!LootFilterKeys.IsValid(filterKey))
                        filterKey = LootFilterKeys.BaseName;

                    if (!pd.LootFilterItemNamesByBox.TryGetValue(filterKey, out var set) || set == null || set.Count == 0)
                        return;

                    set.Clear();
                    _dirty = true;
                    MarkUnionDirty(pd);

                    if (saveNow)
                        Save_NoLock();
                }
            }

            //  used by inventory remover tick ---
            public static HashSet<string> GetLootFilterNamesUnion(string playerId)
            {
                lock (_lock)
                {
                    var pd = Get(playerId);

                    if (pd._cachedUnion == null)
                        pd._cachedUnion = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    if (!pd._unionDirty)
                        return pd._cachedUnion;

                    pd._cachedUnion.Clear();

                    if (pd.LootFilterItemNamesByBox != null)
                    {
                        foreach (var kv in pd.LootFilterItemNamesByBox)
                        {
                            var set = kv.Value;
                            if (set == null || set.Count == 0) continue;

                            foreach (var n in set)
                            {
                                var nn = NormalizeKey(n);
                                if (!string.IsNullOrWhiteSpace(nn))
                                    pd._cachedUnion.Add(nn);
                            }
                        }
                    }

                    pd._unionDirty = false;
                    return pd._cachedUnion;
                }
            }


            public static HashSet<string> GetAllFilterNamesSnapshot()
            {
                lock (_lock)
                {
                    var all = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    foreach (var kv in data)
                    {
                        var pd = kv.Value;
                        if (pd?.LootFilterItemNamesByBox == null) continue;

                        foreach (var box in pd.LootFilterItemNamesByBox.Values)
                        {
                            if (box == null) continue;

                            foreach (var n in box)
                            {
                                var nn = NormalizeKey(n);
                                if (!string.IsNullOrWhiteSpace(nn))
                                    all.Add(nn);
                            }
                        }
                    }

                    return all;
                }
            }

            public static int CountPlayers()
            {
                lock (_lock)
                {
                    return data.Count;
                }
            }

            public static void ClearAllData(bool saveNow = true)
            {
                lock (_lock)
                {
                    if (data.Count == 0)
                        return;

                    data.Clear();
                    _dirty = true;

                    if (saveNow)
                        Save_NoLock();
                }
            }

            public static void ForceSave()
            {
                lock (_lock)
                {
                    _dirty = true;
                    Save_NoLock();
                }
            }
        }
    }
}