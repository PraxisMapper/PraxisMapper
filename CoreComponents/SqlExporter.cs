﻿using CoreComponents.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using MySqlConnector;
using NetTopologySuite.Geometries;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static CoreComponents.DbTables;
using static CoreComponents.Singletons;

namespace CoreComponents
{
    public static class SqlExporter
    {
        //Converts stuff into raw SQL files.
        //Is probably faster than the EntityFramework, and BulkInserts do not get along with Geography columns.

        //IMPORTANT DETAIL:
        //MariaDB stores geometry values as the SRID in 4 bytes, THEN the WKB value (which should start with the byte-order indicator).
        //see: https://dev.mysql.com/doc/refman/8.0/en/gis-data-formats.html
        //This might be why my first attemp was failing.

        //alt 4: another attempt at MySqlBulkLoader in a function? Still have to solve tag issue. TAgs need to be a second file.
        //MySqlBulkLoader bulk = new MySqlBulkLoader(); 

        //Attempt 3: use LOAD DATA INFILE 
        //This needs to remember to save in Tags somehow.

        public static void LoadDataInfileTest(List<StoredOsmElement> items)
        {
            //Write to tab delimited file first, following schema.
            string[] outputData = new string[items.Count()];
            List<string> outputTags = new List<string>();

            for (int i = 0; i < items.Count(); i++)
            {
                outputData[i] = items[i].name + "\t" + items[i].sourceItemID + "\t" + items[i].sourceItemType + "\t" + items[i].elementGeometry.AsText(); // + "\t" + (items[i].IsGameElement ? 1 :0) + "\t" + items[i].AreaSize + "\t" + (items[i].IsGenerated ? 1 : 0) + "\t" + items[i].IsUserProvided ? 1 : 0) //Commented section is not going to apply to osm data.
                foreach(var t in items[i].Tags)
                {
                    outputTags.Add(t.storedOsmElement.id + "\t" + t.Key + "\t" + t.Value);
                }
            }

            //var output = System.IO.File.("loadData.pm");

            //TODO: enable LOCAL command so this could be done to a remote server.
            //TODO: determine path programatically
            //D:\MariaDbData\praxis\

            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            var tempFile = @"D:\MariaDbData\praxis\loadData.pm";  //System.IO.Path.GetTempFileName();
            var tempTags = @"D:\MariaDbData\praxis\loadTags.pm";  //System.IO.Path.GetTempFileName();
            var mariaPath = tempFile.Replace("\\", "\\\\");
            var mariaPathTags = tempTags.Replace("\\", "\\\\");
            System.IO.File.WriteAllLines(tempFile, outputData);
            System.IO.File.WriteAllLines(tempTags, outputTags.ToArray());
            var db = new PraxisContext();
            db.Database.ExecuteSqlRaw("LOAD DATA LOCAL INFILE '" + mariaPath + "' INTO TABLE StoredOsmElements fields terminated by '\t' (name, sourceItemID, sourceItemType, @elementGeometry) SET elementGeometry = ST_GeomFromText(@elementGeometry) ");
            db.Database.ExecuteSqlRaw("LOAD DATA LOCAL INFILE '" + mariaPathTags + "' INTO TABLE ElementTags fields terminated by '\t' (storedOsmElementId, key, value)");
            sw.Stop();
            Console.WriteLine("LOAD DATA command ran in " + sw.Elapsed);
            System.IO.File.Delete(tempFile);
        }

        //Attempt 2: insert the row first, then insert the WKB directly to the column? MariaDB syntax. Is better for single inserts, isn't better than doing batches in EFCore.
        public static void InsertGeomFastTest(StoredOsmElement item)
        {
            var db = new PraxisContext();
            var geoStored = item.elementGeometry;

            //var placehold = new Point(0, 0);
            //item.elementGeometry = placehold;
            //db.StoredOsmElements.Add(item);
            //db.SaveChanges();

            //var sridByteString = "0000" + BitConverter.GetBytes(item.elementGeometry.SRID).ToByteString(); //Might need some padding to hit 4 bytes.
            //string rawSql = "UPDATE StoredOsmElements SET elementGeometry = _binary 0x" + sridByteString + geoStored.ToBinary().ToByteString() + " WHERE id = " + item.id;
            //string rawSql = "UPDATE StoredOsmElements SET elementGeometry = ST_GeomFromWKB('" + geoStored.ToBinary().ToByteString() + "', " + item.elementGeometry.SRID + ") WHERE id = " + item.id; //Might be better than the FromText conversion automatically occurring?
            string rawSql = "INSERT INTO StoredOsmElements(name, sourceItemId, sourceItemType, elementGeometry, isGameElement, AreaSize, IsGenerated, IsUserProvided) VALUES ('"
                + item.name.Replace("'", "''") + "', " + item.sourceItemID + "," + item.sourceItemType + ", ST_GeomFromWKB(X'" + geoStored.ToBinary().ToByteString() + "', " + geoStored.SRID + "), " + item.IsGameElement + "," 
                + geoStored.Area + "," + item.IsGenerated + "," + item.IsUserProvided + ")";
            db.Database.ExecuteSqlRaw(rawSql);
        }

        //Attempt 1: write the SQL file to be run in HeidiSQL. MariaDB syntax.
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