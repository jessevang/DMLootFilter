using System;
using System.Reflection;
using UnityEngine;

namespace DMLootFilter
{
    internal static class InventoryUtil
    {
        public static ItemStack[] GetToolbeltOrNull(EntityPlayer player)
        {
            if (player?.inventory == null)
                return null;

            var inv = player.inventory;

            var arr =
                TryGetItemStackArrayField(inv, "toolbelt") ??
                TryGetItemStackArrayField(inv, "Toolbelt") ??
                TryGetItemStackArrayProperty(inv, "toolbelt") ??
                TryGetItemStackArrayProperty(inv, "Toolbelt") ??
                TryInvokeItemStackArrayMethod(inv, "GetToolbelt") ??
                TryInvokeItemStackArrayMethod(inv, "GetToolbeltItems") ??
                TryInvokeItemStackArrayMethod(inv, "GetToolbeltSlots");

            return arr;
        }

        public static ItemStack[] GetBackpackOrNull(EntityPlayer player)
        {
            if (player?.inventory == null)
                return null;

            var inv = player.inventory;

            var arr =
                TryGetItemStackArrayField(inv, "bag") ??
                TryGetItemStackArrayField(inv, "Bag") ??
                TryGetItemStackArrayField(inv, "backpack") ??
                TryGetItemStackArrayField(inv, "items") ??
                TryGetItemStackArrayProperty(inv, "bag") ??
                TryGetItemStackArrayProperty(inv, "Bag") ??
                TryGetItemStackArrayProperty(inv, "backpack") ??
                TryGetItemStackArrayProperty(inv, "items") ??
                TryInvokeItemStackArrayMethod(inv, "GetBag") ??
                TryInvokeItemStackArrayMethod(inv, "GetBackpack") ??
                TryInvokeItemStackArrayMethod(inv, "GetSlots") ??
                TryInvokeItemStackArrayMethod(inv, "GetItems");

            return arr;
        }

        public static bool TryGetInventory(EntityPlayer player, out ItemStack[] toolbelt, out ItemStack[] backpack)
        {
            toolbelt = GetToolbeltOrNull(player);
            backpack = GetBackpackOrNull(player);

            if (toolbelt == null && backpack == null)
            {
                Debug.Log("[DMLootFilter] CRITICAL: Unable to resolve both toolbelt and backpack inventory arrays.");
                return false;
            }

            return true;
        }

        private static ItemStack[] TryInvokeItemStackArrayMethod(object obj, string methodName)
        {
            try
            {
                var m = obj.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (m == null || m.GetParameters().Length != 0)
                    return null;

                if (!typeof(ItemStack[]).IsAssignableFrom(m.ReturnType))
                    return null;

                return (ItemStack[])m.Invoke(obj, null);
            }
            catch
            {
                return null;
            }
        }

        private static ItemStack[] TryGetItemStackArrayField(object obj, string fieldName)
        {
            try
            {
                var f = obj.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f == null || !typeof(ItemStack[]).IsAssignableFrom(f.FieldType))
                    return null;

                return (ItemStack[])f.GetValue(obj);
            }
            catch
            {
                return null;
            }
        }

        private static ItemStack[] TryGetItemStackArrayProperty(object obj, string propName)
        {
            try
            {
                var p = obj.GetType().GetProperty(propName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p == null || !typeof(ItemStack[]).IsAssignableFrom(p.PropertyType))
                    return null;

                return (ItemStack[])p.GetValue(obj, null);
            }
            catch
            {
                return null;
            }
        }
    }
}
