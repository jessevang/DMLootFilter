using HarmonyLib;
using UnityEngine;

namespace DMLootFilter.Scripts
{
    [HarmonyPatch(typeof(GameManager))]
    public static class LootFilterCollectEntityPatch
    {
        private static readonly AccessTools.FieldRef<GameManager, World> _world =
            AccessTools.FieldRefAccess<GameManager, World>("m_World");

        [HarmonyPrefix]
        [HarmonyPatch(nameof(GameManager.CollectEntityServer))]
        private static bool CollectEntityServer_Prefix(GameManager __instance, int _entityId, int _playerId)
        {
            var cm = SingletonMonoBehaviour<ConnectionManager>.Instance;
            if (cm == null || !cm.IsServer)
                return true;

            World world;
            try { world = _world(__instance); }
            catch { return true; }

            if (world == null)
                return true;

            Entity entity;
            try { entity = world.GetEntity(_entityId); }
            catch { return true; }

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
            int count;
            try
            {
                type = entityItem.itemStack.itemValue.type;
                count = entityItem.itemStack.count;
            }
            catch
            {
                return true;
            }

            string itemKey = "";
            try
            {
                var ic = ItemClass.GetForId(type);
                if (ic != null) itemKey = ic.Name ?? "";
            }
            catch { }

            if (string.IsNullOrWhiteSpace(itemKey))
                return true;

            bool filtered;
            try
            {
                filtered = PlayerDataStore.PlayerStorage.HasFilterName(playerId, itemKey);
            }
            catch
            {
                return true;
            }

            if (!filtered)
                return true;

            Debug.Log($"[DMLootFilter] Blocked pickup. playerId={playerId} entityId={_entityId} itemType={type} itemName='{itemKey}' count={count}");
            return false;
        }
    }
}
