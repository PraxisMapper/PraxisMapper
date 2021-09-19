using PraxisCore;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using static PraxisCore.ConstantValues;

namespace Larry
{
    //DBCommands is where functions that do database work go. This includes reading from JSON files to create/update/delete DB entries.
    public static class DBCommands
    {
        public static void CleanDb()
        {
            Log.WriteLog("Cleaning DB at " + DateTime.Now);
            PraxisContext osm = new PraxisContext();
            osm.Database.SetCommandTimeout(900);

            osm.Database.ExecuteSqlRaw("TRUNCATE TABLE MapData");
            Log.WriteLog("MapData cleaned at " + DateTime.Now, Log.VerbosityLevels.High);
            osm.Database.ExecuteSqlRaw("TRUNCATE TABLE MapTiles");
            Log.WriteLog("MapTiles cleaned at " + DateTime.Now, Log.VerbosityLevels.High);
            osm.Database.ExecuteSqlRaw("TRUNCATE TABLE PerformanceInfo");
            Log.WriteLog("PerformanceInfo cleaned at " + DateTime.Now, Log.VerbosityLevels.High);
            osm.Database.ExecuteSqlRaw("TRUNCATE TABLE GeneratedMapData");
            Log.WriteLog("GeneratedMapData cleaned at " + DateTime.Now, Log.VerbosityLevels.High);
            osm.Database.ExecuteSqlRaw("TRUNCATE TABLE SlippyMapTiles");
            Log.WriteLog("SlippyMapTiles cleaned at " + DateTime.Now, Log.VerbosityLevels.High);
            Log.WriteLog("DB cleaned at " + DateTime.Now);
        }

        public static void FindServerBounds()
        {
            //This is an important command if you don't want to track data outside of your initial area.
            Log.WriteLog("Detecting server map boundaries from data at " + DateTime.Now);
            var results = Place.DetectServerBounds(resolutionCell8); //Using 8 for now.

            var db = new PraxisContext();
            var settings = db.ServerSettings.FirstOrDefault();
            settings.NorthBound = results.NorthLatitude;
            settings.SouthBound = results.SouthLatitude;
            settings.EastBound = results.EastLongitude;
            settings.WestBound = results.WestLongitude;
            db.SaveChanges();
            Log.WriteLog("Server map boundaries found and saved at " + DateTime.Now);
        }

        public static void RemoveDuplicates()
        {
            //I might need to reconsider how i handle duplicates, since different files will have different pieces of some ways.
            //Current plan: process relations bigger than the files I normally use separately from the larger files, store those in their own file.
            Log.WriteLog("Scanning for duplicate entries at " + DateTime.Now);
            var db = new PraxisContext();
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var dupedMapDatas = db.StoredOsmElements.Where(md => md.sourceItemID != null && md.sourceItemType == 2).GroupBy(md => md.sourceItemID)
                .Select(m => new { m.Key, Count = m.Count() })
                .ToDictionary(d => d.Key, v => v.Count)
                .Where(md => md.Value > 1);
            Log.WriteLog("Duped Ways loaded at " + DateTime.Now);

            foreach (var dupe in dupedMapDatas)
            {
                var entriesToDelete = db.StoredOsmElements.Where(md => md.sourceItemID == dupe.Key && md.sourceItemType == 2); //.ToList();
                db.StoredOsmElements.RemoveRange(entriesToDelete.Skip(1));
                db.SaveChanges(); //so the app can make partial progress if it needs to restart
            }
            Log.WriteLog("Duped Way entries deleted at " + DateTime.Now);

            dupedMapDatas = db.StoredOsmElements.Where(md => md.sourceItemID != null && md.sourceItemType == 3).GroupBy(md => md.sourceItemID) //This might require a different approach, or possibly different server settings?
                .Select(m => new { m.Key, Count = m.Count() })
                .ToDictionary(d => d.Key, v => v.Count)
                .Where(md => md.Value > 1);
            Log.WriteLog("Duped Relations loaded at " + DateTime.Now);

            foreach (var dupe in dupedMapDatas)
            {
                var entriesToDelete = db.StoredOsmElements.Where(md => md.sourceItemID == dupe.Key && md.sourceItemID == 3); //.ToList();
                db.StoredOsmElements.RemoveRange(entriesToDelete.Skip(1));
                db.SaveChanges(); //so the app can make partial progress if it needs to restart
            }
            Log.WriteLog("Duped Relation entries deleted at " + DateTime.Now);
        }

        public static void UpdateExistingEntries(string path)
        {
            List<string> filenames = System.IO.Directory.EnumerateFiles(path, "*.json").ToList();
            System.Threading.Tasks.ParallelOptions po = new System.Threading.Tasks.ParallelOptions();
            po.MaxDegreeOfParallelism = 8; //Limit how many running loops at once we have.
            System.Threading.Tasks.Parallel.ForEach(filenames, po, (filename) =>
            {
                try
                {
                    //Similar to the load process, but replaces existing entries instead of only inserting.
                    var db = new PraxisContext();
                    Log.WriteLog("Loading " + filename);
                    var entries = GeometrySupport.ReadStoredElementsFileToMemory(filename);
                    Log.WriteLog(entries.Count() + " entries to update in database for " + filename);

                    int updateCounter = 0;
                    int updateTotal = 0;
                    foreach (var entry in entries)
                    {
                        updateCounter++;
                        updateTotal++;
                        var query = db.StoredOsmElements.AsQueryable();
                        if (entry.sourceItemID != null)
                            query = query.Where(md => md.sourceItemID == entry.sourceItemID && md.sourceItemType == entry.sourceItemType);

                        var existingData = query.ToList();
                        if (existingData.Count() > 0)
                        {
                            foreach (var item in existingData)
                            {
                                item.AreaSize = entry.AreaSize;
                                item.GameElementName = entry.GameElementName;
                                item.IsGameElement = entry.IsGameElement;
                                item.name = entry.name;
                                item.elementGeometry = entry.elementGeometry;
                                item.Tags = entry.Tags;
                            }
                        }
                        else
                        {
                            db.StoredOsmElements.Add(entry);
                        }

                        if (updateCounter > 1000)
                        {
                            db.SaveChanges();
                            db = new PraxisContext();
                            updateCounter = 0;
                            Log.WriteLog(updateTotal + " entries updated to DB");
                        }
                    }
                    db.SaveChanges();
                    System.IO.File.Move(filename, filename + "Done");
                    Log.WriteLog(filename + " completed at " + DateTime.Now);
                }
                catch (Exception ex)
                {
                    Log.WriteLog("Error multithreading: " + ex.Message + ex.StackTrace);
                }
            });
        }

        public static void FixAreaSizes()
        {
            Log.WriteLog("Starting AreaSize fix at  " + DateTime.Now);
            PraxisContext db = new PraxisContext();
            var toFix = db.StoredOsmElements.Where(m => m.AreaSize == null).ToList();
            //var toFix = db.MapData.Where(m => m.MapDataId == 2500925).ToList();
            foreach(var fix in toFix)
                fix.AreaSize = fix.elementGeometry.Length;
            db.SaveChanges();
            Log.WriteLog("AreaSizes updated at  " + DateTime.Now);
        }
    }
}
