using System;
using System.Collections.Generic;
using System.IO;           // Path
using HarmonyLib;
using UnityEngine;

namespace DMLootFilter
{
    public class DMLootFilterMod : IModApi
    {
        private const string HarmonyId = "DMLootFilter.Mod";

        // Ensure we only generate once per server start
        private static bool _generated = false;

        public void InitMod(Mod modInstance)
        {
            try
            {
                // Register events first
                ModEvents.PlayerSpawnedInWorld.RegisterHandler(OnPlayerSpawnedInWorld);
  

                // Load persisted player data
                PlayerDataStore.PlayerStorage.Load();

                // Apply Harmony patches
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

                if (pd.LootFilterItemNames == null)
                    pd.LootFilterItemNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                Debug.Log("[DMLootFilter] Player spawned. id=" + playerId + " filterNames=" + pd.LootFilterItemNames.Count);
            }
            catch (Exception ex)
            {
                Debug.LogError("[DMLootFilter] PlayerSpawned handler error: " + ex);
            }
        }

       
    }
}
