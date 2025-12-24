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

        private static readonly Dictionary<string, string> _actionKeyByLower =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private static bool _warnedEmptyGameEventSequences = false;

        private static float _lastHandleActionErrorTime = -9999f;
        private static readonly float _handleActionErrorThrottleSeconds = 30f;

        private static float _lastRebuildErrorTime = -9999f;
        private static readonly float _rebuildErrorThrottleSeconds = 30f;

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
                RebuildActionCache(now);
            }

            if (_actionKeyByLower.Count == 0)
                return;

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

                if (DMLootFilter.Scripts.LootFilterOpenState.IsSuspended(cInfo.entityId))
                    continue;

                string playerId = PlayerIdUtil.GetPersistentIdOrNull(cInfo);
                if (string.IsNullOrWhiteSpace(playerId))
                    continue;

                var filterNames = PlayerDataStore.PlayerStorage.GetLootFilterNames(playerId);
                if (filterNames == null || filterNames.Count == 0)
                    continue;

                var filterLower = LootFilterInventoryProcessor.BuildFilterLower(filterNames);

                LootFilterSnapshotTracker.TrySpawnPendingDrops(playerId, cInfo, player, filterLower);

                bool didAttemptRemove = false;

                foreach (var itemName in filterNames)
                {
                    if (string.IsNullOrWhiteSpace(itemName))
                        continue;

                    string sanitized = SanitizeForActionName(itemName);
                    string desiredLower = (ActionPrefix + sanitized).ToLowerInvariant();

                    if (!_actionKeyByLower.TryGetValue(desiredLower, out var actualKey))
                        continue;

                    if (!didAttemptRemove)
                    {
                        didAttemptRemove = true;
                        LootFilterSnapshotTracker.SetPendingBeforeSnapshot(playerId, cInfo, filterLower);
                    }

                    try
                    {
                        GameEventManager.Current.HandleAction(actualKey, null, player, false, "");
                    }
                    catch (Exception ex)
                    {
                        if (now - _lastHandleActionErrorTime >= _handleActionErrorThrottleSeconds)
                        {
                            _lastHandleActionErrorTime = now;
                            Debug.Log($"[DMLootFilter] HandleAction failed: {actualKey} ex={ex}");
                        }
                    }
                }
            }
        }

        private static void RebuildActionCache(float now)
        {
            _actionKeyByLower.Clear();

            try
            {
                var dict = GameEventManager.GameEventSequences;
                if (dict == null || dict.Count == 0)
                {
                    if (!_warnedEmptyGameEventSequences)
                    {
                        _warnedEmptyGameEventSequences = true;
                        Debug.Log("[DMLootFilter] GameEventSequences empty. gameevents.xml may not be loaded.");
                    }
                    return;
                }

                foreach (var k in dict.Keys)
                {
                    if (string.IsNullOrWhiteSpace(k))
                        continue;

                    var actual = k.Trim();
                    var lower = actual.ToLowerInvariant();

                    if (!_actionKeyByLower.ContainsKey(lower))
                        _actionKeyByLower[lower] = actual;
                }
            }
            catch (Exception ex)
            {
                if (now - _lastRebuildErrorTime >= _rebuildErrorThrottleSeconds)
                {
                    _lastRebuildErrorTime = now;
                    Debug.Log($"[DMLootFilter] RebuildActionCache failed ex={ex}");
                }
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
