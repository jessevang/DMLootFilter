using HarmonyLib;
using System;
using UnityEngine;

namespace DMLootFilter.Scripts
{
    [HarmonyPatch(typeof(GameManager))]
    public static class LootFilterCollectEntityPatch
    {
        private static readonly AccessTools.FieldRef<GameManager, World> _world =
            AccessTools.FieldRefAccess<GameManager, World>("m_World");

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

        [HarmonyPrefix]
        [HarmonyPatch(nameof(GameManager.CollectEntityServer))]
        private static bool CollectEntityServer_Prefix(GameManager __instance, int _entityId, int _playerId)
        {
            var cm = SingletonMonoBehaviour<ConnectionManager>.Instance;
            if (cm == null || !cm.IsServer)
                return true;

            World world;
            try { world = _world(__instance); }
            catch (Exception ex) { LogCriticalThrottled("CollectEntityServer: world access failed ex=" + ex); return true; }

            if (world == null)
                return true;

            Entity entity;
            try { entity = world.GetEntity(_entityId); }
            catch (Exception ex) { LogCriticalThrottled("CollectEntityServer: GetEntity failed ex=" + ex); return true; }

            var entityItem = entity as EntityItem;
            if (entityItem == null)
                return true;

            var cInfo = LootFilterUtil.TryGetClientInfoByEntityId(_playerId);
            if (cInfo == null)
                return true;

            var playerId = PlayerIdUtil.GetPersistentIdOrNull(cInfo);
            if (string.IsNullOrWhiteSpace(playerId))
                return true;

            int type;
            try { type = entityItem.itemStack.itemValue.type; }
            catch { return true; }

            string itemKey = "";
            try
            {
                var ic = ItemClass.GetForId(type);
                if (ic != null) itemKey = ic.Name ?? "";
            }
            catch
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(itemKey))
                return true;

            bool filtered;
            try { filtered = PlayerDataStore.PlayerStorage.HasFilterName(playerId, itemKey); }
            catch (Exception ex) { LogCriticalThrottled("CollectEntityServer: HasFilterName failed ex=" + ex); return true; }

            if (!filtered)
                return true;

            return false;
        }


    }
}
