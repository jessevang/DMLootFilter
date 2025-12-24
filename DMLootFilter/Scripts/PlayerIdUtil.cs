using System;

namespace DMLootFilter
{
    internal static class PlayerIdUtil
    {

        public static string GetPersistentIdOrNull(ClientInfo cInfo)
        {
            if (cInfo == null)
                return null;

            try
            {
                // EOS crossplay id
                var cross = cInfo.CrossplatformId;
                if (cross != null)
                {
                    string s = cross.CombinedString;
                    if (!string.IsNullOrWhiteSpace(s))
                        return s.Trim();
                }

                // Fallback: platform id (Steam_..., etc)
                var plat = cInfo.PlatformId;
                if (plat != null)
                {
                    string s = plat.CombinedString;
                    if (!string.IsNullOrWhiteSpace(s))
                        return s.Trim();
                }

                // Last resort: internal id
                var internalId = cInfo.InternalId;
                if (internalId != null)
                {
                    string s = internalId.CombinedString;
                    if (!string.IsNullOrWhiteSpace(s))
                        return s.Trim();
                }
            }
            catch
            {
  
            }

            return null;
        }

        public static bool IsEosId(string id)
        {
            return !string.IsNullOrWhiteSpace(id) &&
                   id.StartsWith("EOS_", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsSteamId(string id)
        {
            return !string.IsNullOrWhiteSpace(id) &&
                   id.StartsWith("Steam_", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Convenience helper if you specifically want EOS IDs only.
        /// </summary>
        public static string GetEosIdOrNull(ClientInfo cInfo)
        {
            string id = GetPersistentIdOrNull(cInfo);
            return IsEosId(id) ? id : null;
        }
    }
}
