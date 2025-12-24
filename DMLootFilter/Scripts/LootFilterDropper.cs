// LootFilterDropper.cs
using System;
using UnityEngine;

namespace DMLootFilter
{
    internal static class LootFilterDropper
    {
        public static void DropNearPlayer(EntityPlayer player, ItemStack originalStack, int countToDrop)
        {
            if (player == null || !player.IsSpawned() || player.IsDead()) return;
            if (originalStack == null || originalStack.IsEmpty()) return;
            if (countToDrop <= 0) return;

            var world = GameManager.Instance?.World;
            if (world == null) return;

            ItemValue baseVal = originalStack.itemValue;
            if (baseVal == null || baseVal.IsEmpty()) return;

            int maxStack = 1;
            try
            {
                var ic = baseVal.ItemClass;
                if (ic != null)
                    maxStack = Math.Max(1, ic.Stacknumber.Value);
            }
            catch { maxStack = 1; }

            Vector3 forward = player.GetForwardVector();
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;

            if (right.sqrMagnitude < 0.0001f)
                right = Vector3.right;

            Vector3 pos = player.position + new Vector3(0f, 0.35f, 0f) + forward * 0.85f + right * 0.35f;

            int remaining = countToDrop;
            while (remaining > 0)
            {
                int dropNow = Math.Min(remaining, maxStack);

                ItemValue cloned = baseVal.Clone();
                ItemStack dropStack = new ItemStack(cloned, dropNow);

                SpawnEntityItem(world, player, dropStack, pos);

                remaining -= dropNow;
                pos += right * 0.12f;
            }
        }

        private static void SpawnEntityItem(World world, EntityPlayer player, ItemStack stack, Vector3 pos)
        {
            if (world == null || player == null) return;

            float lifetimeSeconds = 180f;

            EntityItem entityItem = (EntityItem)EntityFactory.CreateEntity(new EntityCreationData
            {
                entityClass = EntityClass.FromString("item"),
                id = EntityFactory.nextEntityID++,
                itemStack = stack,
                pos = pos,
                rot = new Vector3(20f, 0f, 20f),
                lifetime = lifetimeSeconds,
                belongsPlayerId = player.entityId
            });

            world.SpawnEntityInWorld(entityItem);
        }
    }
}
