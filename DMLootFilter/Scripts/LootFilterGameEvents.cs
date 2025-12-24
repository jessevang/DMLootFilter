using System.IO;
using System.Text;

namespace DMLootFilter
{
    internal static class LootFilterGameEvents
    {
        public static string XPathDir;

        public static void EnsureGameEventsXPath()
        {
            if (string.IsNullOrWhiteSpace(XPathDir)) return;

            Directory.CreateDirectory(XPathDir);
            var path = Path.Combine(XPathDir, "gameevents.xml");

            var sb = new StringBuilder();
            sb.AppendLine("<configs>");
            sb.AppendLine();
            sb.AppendLine("<append xpath=\"/gameevents\">");
            sb.AppendLine();
            sb.AppendLine("  <action_sequence name=\"RemoveItemFromInventory\">");
            sb.AppendLine("      <action class=\"RemoveItems\">");
            sb.AppendLine("          <property name=\"items_location\" value=\"Toolbelt,Backpack\" />");
            sb.AppendLine("          <property name=\"items_tags\" value=\"dmlootfilter\" />");
            sb.AppendLine("      </action>");
            sb.AppendLine("  </action_sequence>");
            sb.AppendLine();
            sb.AppendLine("</append>");
            sb.AppendLine();
            sb.AppendLine("</configs>");

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }
    }
}
