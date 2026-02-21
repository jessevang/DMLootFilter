using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace DMLootFilter.Scripts
{
    [HarmonyPatch(typeof(GameManager))]
    public static class LootFilter_TELockUnlockPatches
    {
        private struct ContainerKey : IEquatable<ContainerKey>
        {
            public int lootEntityId;
            public Vector3i blockPos;
            public int clrIdx;

            public static ContainerKey From(int clrIdx, Vector3i pos, int lootEntityId)
            {
                return new ContainerKey
                {
                    clrIdx = clrIdx,
                    blockPos = pos,
                    lootEntityId = lootEntityId
                };
            }

            public bool Equals(ContainerKey other)
            {
                if (lootEntityId != -1 || other.lootEntityId != -1)
                    return lootEntityId == other.lootEntityId;

                return clrIdx == other.clrIdx && blockPos.Equals(other.blockPos);
            }

            public override bool Equals(object obj) => obj is ContainerKey k && Equals(k);

            public override int GetHashCode()
            {
                unchecked
                {
                    if (lootEntityId != -1)
                        return lootEntityId.GetHashCode();

                    int h = 17;
                    h = h * 31 + clrIdx.GetHashCode();
                    h = h * 31 + blockPos.GetHashCode();
                    return h;
                }
            }

            public override string ToString()
            {
                return lootEntityId != -1
                    ? "lootEntityId=" + lootEntityId
                    : "clrIdx=" + clrIdx + " pos=" + blockPos;
            }
        }

        private struct OpenInfo
        {
            public int openerEntityId;
            public bool isFilterBox;
            public string filterKey; // "filter", "filter1"... "filter10"
        }

        private static readonly Dictionary<ContainerKey, OpenInfo> _openByContainer =
            new Dictionary<ContainerKey, OpenInfo>();

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

        [HarmonyPostfix]
        [HarmonyPatch(nameof(GameManager.TELockServer))]
        private static void TELockServer_Postfix(int _clrIdx, Vector3i _blockPos, int _lootEntityId, int _entityIdThatOpenedIt, string _customUi)
        {
            try
            {
                var cm = SingletonMonoBehaviour<ConnectionManager>.Instance;
                if (cm == null || !cm.IsServer)
                    return;

                var key = ContainerKey.From(_clrIdx, _blockPos, _lootEntityId);

                bool isFilter = false;
                string filterKey = null;

                var world = GameManager.Instance?.World;
                if (world != null)
                {
                    TileEntity te = (_lootEntityId != -1) ? world.GetTileEntity(_lootEntityId) : world.GetTileEntity(_blockPos);
                    if (te != null)
                    {
                        string name = LootFilterUtil.GetContainerCustomNameOrEmpty(te);
                        if (LootFilterUtil.TryParseFilterBoxKey(name, out var fk))
                        {
                            isFilter = true;
                            filterKey = fk;
                        }
                    }
                }

                _openByContainer[key] = new OpenInfo
                {
                    openerEntityId = _entityIdThatOpenedIt,
                    isFilterBox = isFilter,
                    filterKey = filterKey
                };

                if (isFilter)
                    LootFilterOpenState.MarkOpen(_entityIdThatOpenedIt);
            }
            catch (Exception ex)
            {
                LogCriticalThrottled("TELockServer_Postfix error: " + ex);
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(GameManager.TEUnlockServer))]
        private static void TEUnlockServer_Postfix(int _clrIdx, Vector3i _blockPos, int _lootEntityId, bool _allowContainerDestroy)
        {
            try
            {
                var cm = SingletonMonoBehaviour<ConnectionManager>.Instance;
                if (cm == null || !cm.IsServer)
                    return;

                var key = ContainerKey.From(_clrIdx, _blockPos, _lootEntityId);

                if (!_openByContainer.TryGetValue(key, out var openInfo) || openInfo.openerEntityId <= 0)
                    return;

                _openByContainer.Remove(key);

                if (openInfo.isFilterBox)
                    LootFilterOpenState.MarkClosed(openInfo.openerEntityId);

                var cInfo = ClientInfoUtil.TryGetClientInfoByEntityId(openInfo.openerEntityId);
                if (cInfo == null)
                    return;

                string playerId = PlayerIdUtil.GetPersistentIdOrNull(cInfo);
                if (string.IsNullOrWhiteSpace(playerId))
                    return;

                var world = GameManager.Instance?.World;
                if (world == null)
                    return;

                TileEntity te = (_lootEntityId != -1) ? world.GetTileEntity(_lootEntityId) : world.GetTileEntity(_blockPos);
                if (te == null)
                    return;

                if (!te.TryGetSelfOrFeature<ITileEntityLootable>(out var lootable) || lootable == null)
                    return;

                if (!lootable.bPlayerStorage)
                    return;

                // Re-parse on close (safer), fallback to stored key
                string name = LootFilterUtil.GetContainerCustomNameOrEmpty(te);
                if (!LootFilterUtil.TryParseFilterBoxKey(name, out var filterKey))
                    filterKey = openInfo.filterKey;

                if (string.IsNullOrWhiteSpace(filterKey))
                    return;

                LootFilterUtil.SnapshotFilterFromLootable(lootable, playerId, filterKey, saveNow: true);
            }
            catch (Exception ex)
            {
                LogCriticalThrottled("TEUnlockServer_Postfix error: " + ex);
            }
        }
    }
}