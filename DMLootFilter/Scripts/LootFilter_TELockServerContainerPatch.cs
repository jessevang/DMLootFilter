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
        }

        private static readonly Dictionary<ContainerKey, OpenInfo> _openByContainer =
            new Dictionary<ContainerKey, OpenInfo>();

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

                // Attempt to resolve TE name *on open* so we can suspend filtering while UI is open
                var world = GameManager.Instance?.World;
                if (world != null)
                {
                    TileEntity te = (_lootEntityId != -1) ? world.GetTileEntity(_lootEntityId) : world.GetTileEntity(_blockPos);
                    if (te != null)
                    {
                        string name = LootFilterUtil.GetContainerCustomNameOrEmpty(te);
                        if (!string.IsNullOrWhiteSpace(name) &&
                            name.Equals(LootFilterUtil.FilterBoxName, StringComparison.OrdinalIgnoreCase))
                        {
                            isFilter = true;
                        }
                    }
                }

                _openByContainer[key] = new OpenInfo { openerEntityId = _entityIdThatOpenedIt, isFilterBox = isFilter };

                if (isFilter)
                {
                    LootFilterOpenState.MarkOpen(_entityIdThatOpenedIt);
                    Debug.Log("[DMLootFilter] TELockServer: OPEN FILTER key=(" + key + ") openerEntityId=" + _entityIdThatOpenedIt + " ui='" + _customUi + "'");
                }
                else
                {
                    Debug.Log("[DMLootFilter] TELockServer: OPEN key=(" + key + ") openerEntityId=" + _entityIdThatOpenedIt + " ui='" + _customUi + "'");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[DMLootFilter] TELockServer_Postfix error: " + ex);
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

                Debug.Log("[DMLootFilter] TEUnlockServer: CLOSE key=(" + key + ") allowDestroy=" + _allowContainerDestroy);

                if (!_openByContainer.TryGetValue(key, out var openInfo) || openInfo.openerEntityId <= 0)
                {
                    Debug.Log("[DMLootFilter] TEUnlockServer: No opener tracked for key=(" + key + ") (skip snapshot).");
                    return;
                }

                _openByContainer.Remove(key);

                // If this was the FILTER box, re-enable filtering for this player now that UI is closed
                if (openInfo.isFilterBox)
                    LootFilterOpenState.MarkClosed(openInfo.openerEntityId);

                var cInfo = ClientInfoUtil.TryGetClientInfoByEntityId(openInfo.openerEntityId);
                if (cInfo == null)
                {
                    Debug.Log("[DMLootFilter] TEUnlockServer: No ClientInfo for openerEntityId=" + openInfo.openerEntityId + " (skip).");
                    return;
                }

                string playerId = PlayerIdUtil.GetPersistentIdOrNull(cInfo);
                if (string.IsNullOrWhiteSpace(playerId))
                {
                    Debug.Log("[DMLootFilter] TEUnlockServer: Could not resolve persistent id for openerEntityId=" + openInfo.openerEntityId + " (skip).");
                    return;
                }

                var world = GameManager.Instance?.World;
                if (world == null)
                {
                    Debug.Log("[DMLootFilter] TEUnlockServer: World null (skip).");
                    return;
                }

                TileEntity te = (_lootEntityId != -1) ? world.GetTileEntity(_lootEntityId) : world.GetTileEntity(_blockPos);
                if (te == null)
                {
                    Debug.Log("[DMLootFilter] TEUnlockServer: TileEntity null for key=(" + key + ") (skip).");
                    return;
                }

                if (!te.TryGetSelfOrFeature<ITileEntityLootable>(out var lootable) || lootable == null)
                {
                    Debug.Log("[DMLootFilter] TEUnlockServer: TE type=" + te.GetType().Name + " has no ITileEntityLootable feature (skip).");
                    return;
                }

                if (!lootable.bPlayerStorage)
                {
                    Debug.Log("[DMLootFilter] TEUnlockServer: lootable type=" + lootable.GetType().Name + " bPlayerStorage=false (skip).");
                    return;
                }

                string name2 = LootFilterUtil.GetContainerCustomNameOrEmpty(te);
                Debug.Log("[DMLootFilter] TEUnlockServer: containerName='" + name2 + "' playerId=" + playerId + " teType=" + te.GetType().Name + " lootableType=" + lootable.GetType().Name);

                if (string.IsNullOrWhiteSpace(name2) ||
                    !name2.Equals(LootFilterUtil.FilterBoxName, StringComparison.OrdinalIgnoreCase))
                {
                    Debug.Log("[DMLootFilter] TEUnlockServer: Not filter chest (skip).");
                    return;
                }

                Debug.Log("[DMLootFilter] TEUnlockServer: Filter chest closed by " + playerId + ". Snapshotting contents...");
                LootFilterUtil.SnapshotFilterFromLootable(lootable, playerId, saveNow: true);
            }
            catch (Exception ex)
            {
                Debug.LogError("[DMLootFilter] TEUnlockServer_Postfix error: " + ex);
            }
        }
    }
}
