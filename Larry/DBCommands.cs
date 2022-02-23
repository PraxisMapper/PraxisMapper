using Google.OpenLocationCode;
using PraxisCore;
using System;
using System.Collections.Generic;
using System.Linq;
using static PraxisCore.ConstantValues;
using static PraxisCore.DbTables;

namespace Larry
{
    //DBCommands is where functions that do database work go. This includes reading from JSON files to create/update/delete DB entries.
    public static class DBCommands
    {
        public static GeoArea FindServerBounds(long singleArea)
        {
            //This is an important command if you don't want to track data outside of your initial area.
            Log.WriteLog("Detecting server map boundaries from data at " + DateTime.Now);
            var db = new PraxisContext();
            GeoArea results = null;
            if (singleArea != 0)
            {
                var area = db.StoredOsmElements.First(e => e.sourceItemID == singleArea);
                var envelop = area.elementGeometry.EnvelopeInternal;
                results = new GeoArea(envelop.MinY, envelop.MinX, envelop.MaxY, envelop.MaxX);
            }
            else
                results = Place.DetectServerBounds(resolutionCell8); //Using 8 for now.

            var settings = db.ServerSettings.FirstOrDefault();
            settings.NorthBound = results.NorthLatitude;
            settings.SouthBound = results.SouthLatitude;
            settings.EastBound = results.EastLongitude;
            settings.WestBound = results.WestLongitude;
            db.SaveChanges();
            Log.WriteLog("Server map boundaries found and saved at " + DateTime.Now);
            return results;
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
                    //Similar to the load process, but updates existing entries instead of only inserting.
                    var db = new PraxisContext();
                    Log.WriteLog("Loading " + filename);
                    var entries = GeometrySupport.ReadStoredElementsFileToMemory(filename);
                    Log.WriteLog(entries.Count() + " entries to update in database for " + filename);

                    int updateCounter = 0;
                    int updateTotal = 0;
                    foreach (var entry in entries)
                    {
                        //check existing entry, see if it requires being updated
                        var existingData = db.StoredOsmElements.FirstOrDefault(md => md.sourceItemID == entry.sourceItemID && md.sourceItemType == entry.sourceItemType);
                        if (existingData != null)
                        {
                            if (existingData.AreaSize != entry.AreaSize) existingData.AreaSize = entry.AreaSize;
                            if (existingData.GameElementName != entry.GameElementName) existingData.GameElementName = entry.GameElementName;
                            if (existingData.IsGameElement != entry.IsGameElement) existingData.IsGameElement = entry.IsGameElement;
                            //if (existingData.name != entry.name) existingData.name = entry.name;

                            bool expireTiles = false;

                            if (!existingData.elementGeometry.EqualsTopologically(entry.elementGeometry)) //TODO: this might need to be EqualsExact?
                            {
                                //update the geometry for this object.
                                existingData.elementGeometry = entry.elementGeometry;
                                expireTiles = true;
                            }
                            if (!existingData.Tags.SequenceEqual(entry.Tags))
                            {
                                existingData.Tags = entry.Tags;
                                var styleA = TagParser.GetStyleForOsmWay(existingData.Tags);
                                var styleB = TagParser.GetStyleForOsmWay(entry.Tags);
                                if (styleA != styleB)
                                    expireTiles = true; //don't force a redraw on tags unless we change our drawing rules.
                            }
                            
                            if (expireTiles) //geometry or style has to change, otherwise we can skip this step
                            {
                                db.SaveChanges(); //save before expiring, so the next redraw absolutely has the latest data. Can't catch it mid-command if we do this first.
                                db.ExpireMapTiles(entry.elementGeometry, "mapTiles");
                                db.ExpireSlippyMapTiles(entry.elementGeometry, "mapTiles");
                            }
                        }
                        else
                        {
                            //We don't have this item, add it.
                            db.StoredOsmElements.Add(entry);
                            db.SaveChanges(); //again, necessary here to get tiles to draw correctly after expiring.
                            db.ExpireMapTiles(entry.elementGeometry, "mapTiles");
                            db.ExpireSlippyMapTiles(entry.elementGeometry, "mapTiles");
                        }

                        updateCounter++;
                        updateTotal++;

                        //This resets the entity's internal graph to minimize memory growth over time.
                        if (updateCounter > 1000)
                        {
                            db.SaveChanges(); // catch any changes that haven't been saved yet
                            db = new PraxisContext();
                            updateCounter = 0;
                            Log.WriteLog(updateTotal + " entries updated to DB");
                        }
                    }
                    db.SaveChanges(); //final one for anything not yet persisted.
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
            foreach (var fix in toFix)
                fix.AreaSize = fix.elementGeometry.Length;
            db.SaveChanges();
            Log.WriteLog("AreaSizes updated at  " + DateTime.Now);
        }
    }
}
