using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace DMLootFilter
{
    internal static class PlayerDataStore
    {
        public class PlayerData
        {
            public string playerId;
            public HashSet<string> LootFilterItemNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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

                if (pd.LootFilterItemNames == null)
                    pd.LootFilterItemNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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

                            var normalizedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            if (pd.LootFilterItemNames != null)
                            {
                                foreach (var n in pd.LootFilterItemNames)
                                {
                                    var nn = NormalizeKey(n);
                                    if (string.IsNullOrWhiteSpace(nn)) continue;
                                    normalizedNames.Add(nn);
                                }
                            }
                            pd.LootFilterItemNames = normalizedNames;

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

                        var normalizedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        if (pd.LootFilterItemNames != null)
                        {
                            foreach (var n in pd.LootFilterItemNames)
                            {
                                var nn = NormalizeKey(n);
                                if (string.IsNullOrWhiteSpace(nn)) continue;
                                normalizedNames.Add(nn);
                            }
                        }
                        pd.LootFilterItemNames = normalizedNames;

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

            private static string TypeToKey(int itemType)
            {
                try
                {
                    var ic = ItemClass.GetForId(itemType);
                    if (ic == null) return null;
                    return NormalizeKey(ic.Name);
                }
                catch
                {
                    return null;
                }
            }

            public static HashSet<string> GetLootFilterNames(string playerId)
            {
                lock (_lock)
                {
                    var pd = Get(playerId);
                    return pd.LootFilterItemNames;
                }
            }

            public static bool HasFilterName(string playerId, string itemNameKey)
            {
                lock (_lock)
                {
                    var pd = Get(playerId);
                    var k = NormalizeKey(itemNameKey);
                    return !string.IsNullOrWhiteSpace(k) &&
                           pd.LootFilterItemNames != null &&
                           pd.LootFilterItemNames.Contains(k);
                }
            }

            public static bool AddFilterName(string playerId, string itemNameKey, bool saveNow = true)
            {
                lock (_lock)
                {
                    var pd = Get(playerId);

                    if (pd.LootFilterItemNames == null)
                        pd.LootFilterItemNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    var k = NormalizeKey(itemNameKey);
                    if (string.IsNullOrWhiteSpace(k))
                        return false;

                    bool added = pd.LootFilterItemNames.Add(k);
                    if (added) _dirty = true;
                    if (added && saveNow) Save_NoLock();
                    return added;
                }
            }

            public static bool RemoveFilterName(string playerId, string itemNameKey, bool saveNow = true)
            {
                lock (_lock)
                {
                    var pd = Get(playerId);

                    if (pd.LootFilterItemNames == null)
                        pd.LootFilterItemNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    var k = NormalizeKey(itemNameKey);
                    if (string.IsNullOrWhiteSpace(k))
                        return false;

                    bool removed = pd.LootFilterItemNames.Remove(k);
                    if (removed) _dirty = true;
                    if (removed && saveNow) Save_NoLock();
                    return removed;
                }
            }

            public static void SetLootFilterNames(string playerId, IEnumerable<string> itemNameKeys, bool saveNow = true)
            {
                lock (_lock)
                {
                    var pd = Get(playerId);

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

                    bool changed = (pd.LootFilterItemNames == null) || !pd.LootFilterItemNames.SetEquals(set);
                    pd.LootFilterItemNames = set;

                    if (changed) _dirty = true;
                    if (changed && saveNow) Save_NoLock();
                }
            }

            public static void ClearLootFilter(string playerId, bool saveNow = true)
            {
                lock (_lock)
                {
                    var pd = Get(playerId);

                    if (pd.LootFilterItemNames == null)
                        pd.LootFilterItemNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    if (pd.LootFilterItemNames.Count == 0)
                        return;

                    pd.LootFilterItemNames.Clear();
                    _dirty = true;

                    if (saveNow)
                        Save_NoLock();
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

            public static bool HasFilterType(string playerId, int itemType)
            {
                var key = TypeToKey(itemType);
                if (string.IsNullOrWhiteSpace(key))
                    return false;
                return HasFilterName(playerId, key);
            }

            public static HashSet<int> GetLootFilterTypes(string playerId)
            {
                lock (_lock)
                {
                    var names = GetLootFilterNames(playerId);
                    var set = new HashSet<int>();

                    if (names == null || names.Count == 0)
                        return set;

                    foreach (var k in names)
                    {
                        try
                        {
                            var ic = ItemClass.GetItemClass(k);
                            if (ic == null) continue;
                            set.Add(ic.Id);
                        }
                        catch { }
                    }

                    return set;
                }
            }

            public static void SetLootFilterTypes(string playerId, IEnumerable<int> itemTypes, bool saveNow = true)
            {
                var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (itemTypes != null)
                {
                    foreach (var t in itemTypes)
                    {
                        var k = TypeToKey(t);
                        if (string.IsNullOrWhiteSpace(k)) continue;
                        keys.Add(k);
                    }
                }

                SetLootFilterNames(playerId, keys, saveNow);
            }

            public static HashSet<string> GetAllFilterNamesSnapshot()
            {
                lock (_lock)
                {
                    var all = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    foreach (var kv in data)
                    {
                        var pd = kv.Value;
                        if (pd?.LootFilterItemNames == null) continue;

                        foreach (var n in pd.LootFilterItemNames)
                        {
                            var nn = NormalizeKey(n);
                            if (!string.IsNullOrWhiteSpace(nn))
                                all.Add(nn);
                        }
                    }

                    return all;
                }
            }

        }
    }
}
