using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace DMLootFilter
{
    [HarmonyPatch(typeof(GameManager), "Update")]
    public class Patch_GameManager_Update_ItemFilterWatcher
    {
        private static float _lastRunTime = 0f;
        private static readonly float _runIntervalSeconds = 1.0f;

        private const string ActionPrefix = "remove_";

        private static float _lastActionCacheTime = 0f;
        private static readonly float _actionCacheIntervalSeconds = 10f;

        // lower(actionName) -> actual key as stored in GameEventSequences
        private static readonly Dictionary<string, string> _actionKeyByLower =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        static void Postfix()
        {
            var cm = SingletonMonoBehaviour<ConnectionManager>.Instance;
            if (cm == null || !cm.IsServer)
                return;

            var gm = GameManager.Instance;
            if (gm?.gameStateManager == null || !gm.gameStateManager.IsGameStarted())
                return;

            float now = Time.time;
            if (now - _lastRunTime < _runIntervalSeconds)
                return;
            _lastRunTime = now;

            if (now - _lastActionCacheTime >= _actionCacheIntervalSeconds)
            {
                _lastActionCacheTime = now;
                RebuildActionCache();
            }

            var clients = cm.Clients?.List;
            if (clients == null || clients.Count == 0)
                return;

            foreach (var cInfo in clients)
            {
                if (cInfo == null || !cInfo.loginDone)
                    continue;

                if (!GameManager.Instance.World.Players.dict.TryGetValue(cInfo.entityId, out var player))
                    continue;

                if (player == null || !player.IsSpawned() || player.IsDead())
                    continue;

                // If player is currently editing the FILTER chest, pause filtering for them.
                // (Prevents immediately deleting items they pull out of the filter box.)
                if (DMLootFilter.Scripts.LootFilterOpenState.IsSuspended(cInfo.entityId))
                    continue;

                string playerId = PlayerIdUtil.GetPersistentIdOrNull(cInfo);
                if (string.IsNullOrWhiteSpace(playerId))
                    continue;

                var filterNames = PlayerDataStore.PlayerStorage.GetLootFilterNames(playerId);
                if (filterNames == null || filterNames.Count == 0)
                    continue;

                foreach (var itemName in filterNames)
                {
                    if (string.IsNullOrWhiteSpace(itemName))
                        continue;

                    // Build action name from player-data item key
                    string sanitized = SanitizeForActionName(itemName);
                    string desired = ActionPrefix + sanitized;             // e.g. remove_resourceWood
                    string desiredLower = desired.ToLowerInvariant();      // e.g. remove_resourcewood

                    if (!_actionKeyByLower.TryGetValue(desiredLower, out var actualKey))
                    {
                        // Uncomment if you want to see what's missing:
                        // Debug.Log($"[DMLootFilter] Missing action in GameEventSequences: '{desired}' (lower='{desiredLower}')");
                        continue;
                    }

                    try
                    {
                        // IMPORTANT: use the actual dictionary key, not our constructed casing
                        GameEventManager.Current.HandleAction(actualKey, null, player, false, "");
                    }
                    catch (Exception ex)
                    {
                        Debug.Log($"[DMLootFilter] HandleAction failed. action='{actualKey}' desired='{desired}' ex={ex}");
                    }
                }
            }
        }

        private static void RebuildActionCache()
        {
            _actionKeyByLower.Clear();

            try
            {
                var dict = GameEventManager.GameEventSequences;
                if (dict == null || dict.Count == 0)
                {
                    Debug.Log("[DMLootFilter] GameEventSequences is empty. Your Config/gameevents.xml may not be loading.");
                    return;
                }

                foreach (var k in dict.Keys)
                {
                    if (string.IsNullOrWhiteSpace(k)) continue;
                    var actual = k.Trim();
                    var lower = actual.ToLowerInvariant();

                    // keep first seen
                    if (!_actionKeyByLower.ContainsKey(lower))
                        _actionKeyByLower[lower] = actual;
                }

                Debug.Log($"[DMLootFilter] Action cache refreshed. LoadedSequences={dict.Count} Cached={_actionKeyByLower.Count}");
            }
            catch (Exception ex)
            {
                Debug.Log($"[DMLootFilter] RebuildActionCache failed. ex={ex}");
            }
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
    }
}
