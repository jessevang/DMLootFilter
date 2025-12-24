using System;
using System.Collections.Generic;

namespace DMLootFilter.Scripts
{
    internal static class LootFilterOpenState
    {

        private static readonly Dictionary<int, int> _openCounts = new Dictionary<int, int>();

        private static readonly object _lock = new object();

        public static void MarkOpen(int openerEntityId)
        {
            if (openerEntityId <= 0) return;

            lock (_lock)
            {
                _openCounts.TryGetValue(openerEntityId, out int c);
                _openCounts[openerEntityId] = c + 1;
            }
        }

        public static void MarkClosed(int openerEntityId)
        {
            if (openerEntityId <= 0) return;

            lock (_lock)
            {
                if (!_openCounts.TryGetValue(openerEntityId, out int c))
                    return;

                c--;
                if (c <= 0)
                    _openCounts.Remove(openerEntityId);
                else
                    _openCounts[openerEntityId] = c;
            }
        }

        public static bool IsSuspended(int openerEntityId)
        {
            if (openerEntityId <= 0) return false;

            lock (_lock)
                return _openCounts.ContainsKey(openerEntityId);
        }


        public static void Clear(int openerEntityId)
        {
            if (openerEntityId <= 0) return;

            lock (_lock)
                _openCounts.Remove(openerEntityId);
        }
    }
}
