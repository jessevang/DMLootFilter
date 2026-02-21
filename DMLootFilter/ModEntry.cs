using System;
using HarmonyLib;
using UnityEngine;

namespace DMLootFilter
{
    public class DMLootFilterMod : IModApi
    {
        private const string HarmonyId = "DMLootFilter.Mod";

        public void InitMod(Mod modInstance)
        {
            try
            {
                ModEvents.PlayerSpawnedInWorld.RegisterHandler(OnPlayerSpawnedInWorld);

                PlayerDataStore.PlayerStorage.Load();

                var harmony = new Harmony(HarmonyId);
                harmony.PatchAll();

                Debug.Log("[DMLootFilter] InitMod complete: player data loaded, Harmony patches applied, events registered.");
            }
            catch (Exception ex)
            {
                Debug.LogError("[DMLootFilter] InitMod failed: " + ex);
            }
        }

        private void OnPlayerSpawnedInWorld(ref ModEvents.SPlayerSpawnedInWorldData data)
        {
            try
            {
                var cm = SingletonMonoBehaviour<ConnectionManager>.Instance;
                if (cm == null || !cm.IsServer)
                    return;

                var cInfo = data.ClientInfo;
                if (cInfo == null)
                    return;

                string playerId = PlayerIdUtil.GetPersistentIdOrNull(cInfo);
                if (string.IsNullOrWhiteSpace(playerId))
                    return;

                var pd = PlayerDataStore.PlayerStorage.Get(playerId, saveIfNew: true);

                // Union count across all filter boxes
                var union = PlayerDataStore.PlayerStorage.GetLootFilterNamesUnion(playerId);

                int unionCount = union?.Count ?? 0;

                Debug.Log("[DMLootFilter] Player spawned. id=" + playerId + " filterUnionNames=" + unionCount);
            }
            catch (Exception ex)
            {
                Debug.LogError("[DMLootFilter] PlayerSpawned handler error: " + ex);
            }
        }
    }
}