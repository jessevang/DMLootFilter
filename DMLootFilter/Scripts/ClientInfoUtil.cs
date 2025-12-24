using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace DMLootFilter
{
    internal static class ClientInfoUtil
    {
        private static readonly FieldInfo _clientsField =
            AccessTools.Field(typeof(ConnectionManager), "Clients")
            ?? AccessTools.Field(typeof(ConnectionManager), "clients");

        public static ClientInfo TryGetClientInfoByEntityId(int entityId)
        {
            var cm = SingletonMonoBehaviour<ConnectionManager>.Instance;
            if (cm == null) return null;

            // 1) Preferred fast path (works on dedi): cm.Clients.List
            try
            {
                var list = cm.Clients != null ? cm.Clients.List : null;
                if (list != null)
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        var ci = list[i];
                        if (ci != null && ci.entityId == entityId)
                            return ci;
                    }
                }
            }
            catch { }

            // 2) Reflection fallback: field "Clients"/"clients" (handles older/odd variants)
            try
            {
                if (_clientsField == null) return null;

                object clientsObj = _clientsField.GetValue(cm);
                if (clientsObj == null) return null;

                var enumerable = clientsObj as IEnumerable;
                if (enumerable != null)
                {
                    foreach (object o in enumerable)
                    {
                        var ci = o as ClientInfo;
                        if (ci != null && ci.entityId == entityId)
                            return ci;
                    }
                    return null;
                }

                var dict = clientsObj as IDictionary;
                if (dict != null)
                {
                    foreach (DictionaryEntry de in dict)
                    {
                        var ci = de.Value as ClientInfo;
                        if (ci != null && ci.entityId == entityId)
                            return ci;
                    }
                }
            }
            catch { }

            return null;
        }
    }
}
