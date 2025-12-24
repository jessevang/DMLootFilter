using System;
using System.Collections.Generic;
using System.IO;           // <-- needed for Path
using HarmonyLib;
using UnityEngine;

namespace DMLootFilter
{
    public class DMLootFilterMod : IModApi
    {
        private const string HarmonyId = "DMLootFilter.Mod";

        public void InitMod(Mod modInstance)
        {
            ModEvents.PlayerSpawnedInWorld.RegisterHandler(OnPlayerSpawnedInWorld);

            try
            {
                PlayerDataStore.PlayerStorage.Load();

                var harmony = new Harmony(HarmonyId);
                harmony.PatchAll();

                Debug.Log("[DMLootFilter] InitMod complete: player data loaded, Harmony patches applied.");


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
