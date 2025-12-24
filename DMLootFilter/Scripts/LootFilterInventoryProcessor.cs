
using System;
using System.Collections.Generic;
using UnityEngine;

namespace DMLootFilter
{
    internal static class LootFilterInventoryProcessor
    {
        public static HashSet<string> BuildFilterLower(IReadOnlyCollection<string> filterNames)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (filterNames == null) return set;

            foreach (var nm in filterNames)
            {
                if (!string.IsNullOrWhiteSpace(nm))
                    set.Add(nm.Trim().ToLowerInvariant());
            }

            return set;
        }
    }
}
