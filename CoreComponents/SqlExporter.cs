using CoreComponents.Support;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static CoreComponents.DbTables;
using static CoreComponents.Singletons;

namespace CoreComponents
{
    public static class SqlExporter
    {
        //Converts stuff into raw SQL files.
        //Is probably faster than the EntityFramework, and BulkInserts do not get along with Geography columns.

        public static void DumpToSql(List<StoredOsmElement> elements, string filename)
        {
            //Assuming that we're getting a limited set of elements
            StringBuilder sb = new StringBuilder();

            //MariaDB
            //These are added by HeidiSQL, so I'm including them here too.
            sb.AppendLine("/*!40101 SET @OLD_CHARACTER_SET_CLIENT=@@CHARACTER_SET_CLIENT */;");
            sb.AppendLine("/*!40101 SET NAMES utf8 */;");
            sb.AppendLine("/*!50503 SET NAMES utf8mb4 */;");
            sb.AppendLine("/*!40014 SET @OLD_FOREIGN_KEY_CHECKS=@@FOREIGN_KEY_CHECKS, FOREIGN_KEY_CHECKS=0 */;");
            sb.AppendLine("/*!40101 SET @OLD_SQL_MODE=@@SQL_MODE, SQL_MODE='NO_AUTO_VALUE_ON_ZERO' */;");
            sb.AppendLine("/*!40111 SET @OLD_SQL_NOTES=@@SQL_NOTES, SQL_NOTES=0 */;");
            sb.AppendLine("/*!40000 ALTER TABLE `StoredOsmElements` DISABLE KEYS */;");

            sb.AppendLine("INSERT IGNORE INTO `StoredOsmElements` (`name`, `sourceItemId`, `sourceItemType`, `elementGeometry`, `AreaSize`, `IsGenerated`, `IsUserProvided`) VALUES");

            var e1 = elements.First();
            sb.AppendLine("('" + e1.name + "', " + e1.sourceItemID + ", " + e1.sourceItemType + ", _binary 0x" + e1.elementGeometry.ToBinary().ToByteString() + ", " + e1.elementGeometry.Area + ", " + (e1.IsGenerated ? "1" : "0") + ", " + (e1.IsUserProvided ? "1" : "0") + ")");

            //HeidiSQL likes to keep inserts at 1MB or less when possible. Attempt to duplicate that?
            foreach (var e in elements.Skip(1))
            {
                //Geometry elements are handled as "_binary 0xFFEEFFDDEEFF" so I want the WKB for each element.
                sb.AppendLine(",('" + e.name + "', " + e.sourceItemID + ", " + e.sourceItemType + ", _binary 0x" + e.elementGeometry.ToBinary().ToByteString() + ", " + e.elementGeometry.Area + ", " + (e.IsGenerated ? "1" : "0") + ", " + (e.IsUserProvided ? "1" : "0") + ")");
            }


            sb.Append(";");


            System.IO.File.WriteAllText(filename, sb.ToString());
        }


    }
}
