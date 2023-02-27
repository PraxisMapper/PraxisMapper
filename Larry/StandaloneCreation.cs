using Google.OpenLocationCode;
using PraxisCore;
using PraxisCore.Standalone;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static PraxisCore.Standalone.StandaloneDbTables;

namespace Larry
{
    internal class StandaloneCreation
    {
        //These functions are for taking a PraxisMapper DB and making a workable mobile version. Much less accurate, requires 0 data connectivity.
        //No longer a high priority but I do want to keep this code around.

        public static void CreateStandaloneDB(long relationID = 0, GeoArea bounds = null, bool saveToDB = false, bool saveToFolder = true)
        {
            string name = "";
            if (bounds != null)
                name = Math.Truncate(bounds.SouthLatitude) + "_" + Math.Truncate(bounds.WestLongitude) + "_" + Math.Truncate(bounds.NorthLatitude) + "_" + Math.Truncate(bounds.EastLongitude) + ".sqlite";

            if (relationID > 0)
                name = relationID.ToString() + ".sqlite";

            if (File.Exists(name))
                File.Delete(name);

            var mainDb = new PraxisContext();
            var sqliteDb = new StandaloneContext(relationID.ToString());
            sqliteDb.ChangeTracker.AutoDetectChangesEnabled = false;
            sqliteDb.Database.EnsureCreated();
            Log.WriteLog("Standalone DB created for relation " + relationID + " at " + DateTime.Now);

            GeoArea buffered;
            if (relationID > 0)
            {
                var fullArea = mainDb.Places.FirstOrDefault(m => m.SourceItemID == relationID && m.SourceItemType == 3);
                if (fullArea == null)
                    return;

                buffered = Converters.GeometryToGeoArea(fullArea.ElementGeometry);
                //This should also be able to take a bounding box in addition in the future.
            }
            else
                buffered = bounds;

            //TODO: set a flag to allow this to pull straight from a PBF file? 
            List<DbTables.Place> allPlaces = new List<DbTables.Place>();
            var intersectCheck = Converters.GeoAreaToPolygon(buffered);
            bool pullFromPbf = false; //Set via arg at startup? or setting file?
            if (!pullFromPbf)
                allPlaces = Place.GetPlaces(buffered);
            else
            {
                //need a file to read from.
                //optionally a bounding box on that file.
                //Starting to think i might want to track some generic parameters I refer to later. like -box|s|w|n|e or -point|lat|long or -singleFile|here.osm.pbf
                //allPlaces = PbfFileParser.ProcessSkipDatabase();
            }

            Log.WriteLog("Loaded all intersecting geometry at " + DateTime.Now);

            string minCode = new OpenLocationCode(buffered.SouthLatitude, buffered.WestLongitude).CodeDigits;
            string maxCode = new OpenLocationCode(buffered.NorthLatitude, buffered.EastLongitude).CodeDigits;
            int removableLetters = 0;
            for (int i = 0; i < 10; i++)
            {
                if (minCode[i] == maxCode[i])
                    removableLetters++;
                else
                    i += 10;
            }
            string commonStart = minCode.Substring(0, removableLetters);

            var wikiList = allPlaces.Where(a => a.Tags.Any(t => t.Key == "wikipedia") && TagParser.GetName(a) != "").Select(a => TagParser.GetName(a)).Distinct().ToList();
            //Leaving this nearly wide open, since it's not the main driver of DB size.
            var basePlaces = allPlaces.Where(a => TagParser.GetName(a) != "" || a.StyleName != "unmatched").ToList(); //.Where(a => a.name != "").ToList();// && (a.IsGameElement || wikiList.Contains(a.name))).ToList();
            var distinctNames = basePlaces.Select(p => TagParser.GetName(p)).Distinct().ToList();//This distinct might be causing things in multiple pieces to only detect one of them, not all of them?

            var placeInfo = Standalone.GetPlaceInfo(basePlaces);
            //Remove trails later.
            //SHORTCUT: for roads that are a straight-enough line (under 1 Cell10 in width or height)
            //just treat them as being 1 Cell10 in that axis, and skip tracking them by each Cell10 they cover.
            HashSet<long> skipEntries = new HashSet<long>();
            foreach (var pi in placeInfo.Where(p => p.areaType == "road" || p.areaType == "trail"))
            {
                //If a road is nearly a straight line, treat it as though it was 1 cell10 wide, and don't index its coverage per-cell later.
                if (pi.height <= ConstantValues.resolutionCell10 && pi.width >= ConstantValues.resolutionCell10)
                { pi.height = ConstantValues.resolutionCell10; skipEntries.Add(pi.PlaceId); }
                else if (pi.height >= ConstantValues.resolutionCell10 && pi.width <= ConstantValues.resolutionCell10)
                { pi.width = ConstantValues.resolutionCell10; skipEntries.Add(pi.PlaceId); }
            }

            sqliteDb.PlaceInfo2s.AddRange(placeInfo);
            sqliteDb.SaveChanges();
            Log.WriteLog("Processed geometry at " + DateTime.Now);
            var placeDictionary = placeInfo.ToDictionary(k => k.PlaceId, v => v);

            //to save time, i need to index which areas are in which Cell6.
            //So i know which entries I can skip when running.
            var indexCell6 = Standalone.IndexAreasPerCell6(buffered, basePlaces);
            var indexes = indexCell6.SelectMany(i => i.Value.Select(v => new PlaceIndex() { PlusCode = i.Key, placeInfoId = placeDictionary[v.SourceItemID].id })).ToList();
            sqliteDb.PlaceIndexs.AddRange(indexes);

            sqliteDb.SaveChanges();
            Log.WriteLog("Processed Cell6 index table at " + DateTime.Now);

            //trails need processed the old way, per Cell10, when they're not simply a straight-line.
            //Roads too.
            var tdSmalls = new Dictionary<string, TerrainDataSmall>(); //Possible issue: a trail and a road with the same name would only show up as whichever one got in the DB first.
            var toRemove = new List<PlaceInfo2>();
            foreach (var trail in basePlaces.Where(p => (p.StyleName == "trail" || p.StyleName == "road"))) //TODO: add rivers here?
            {
                if (skipEntries.Contains(trail.SourceItemID))
                    continue; //Don't per-cell index this one, we shifted it's envelope to handle it instead.

                if (TagParser.GetName(trail) == "")
                    continue; //So sorry, but there's too damn many roads without names inflating DB size without being useful as-is.

                var p = placeDictionary[trail.SourceItemID];
                toRemove.Add(p);

                GeoArea thisPath = Converters.GeometryToGeoArea(trail.ElementGeometry);
                List<DbTables.Place> oneEntry = new List<DbTables.Place>();
                oneEntry.Add(trail);

                var overlapped = AreaStyle.GetAreaDetails(ref thisPath, ref oneEntry);
                if (overlapped.Count > 0)
                {
                    tdSmalls.TryAdd(TagParser.GetName(trail), new TerrainDataSmall() { Name = TagParser.GetName(trail), areaType = trail.StyleName });
                }
                foreach (var o in overlapped)
                {
                    var ti = new StandaloneTerrainInfo();
                    ti.PlusCode = o.plusCode.Substring(removableLetters, 10 - removableLetters);
                    ti.TerrainDataSmall = new List<TerrainDataSmall>();
                    ti.TerrainDataSmall.Add(tdSmalls[o.data.name]);
                    sqliteDb.TerrainInfo.Add(ti);
                }
                sqliteDb.SaveChanges();
            }

            foreach (var r in toRemove.Distinct())
                sqliteDb.PlaceInfo2s.Remove(r);
            sqliteDb.SaveChanges();
            Log.WriteLog("Trails processed at " + DateTime.Now);

            //make scavenger hunts
            var sh = Standalone.GetScavengerHunts(allPlaces);
            sqliteDb.ScavengerHunts.AddRange(sh);
            sqliteDb.SaveChanges();
            Log.WriteLog("Auto-created scavenger hunt entries at " + DateTime.Now);

            var swCorner = new OpenLocationCode(intersectCheck.EnvelopeInternal.MinY, intersectCheck.EnvelopeInternal.MinX);
            var neCorner = new OpenLocationCode(intersectCheck.EnvelopeInternal.MaxY, intersectCheck.EnvelopeInternal.MaxX);
            //insert default entries for a new player.
            sqliteDb.PlayerStats.Add(new PlayerStats() { timePlayed = 0, distanceWalked = 0, score = 0 });
            sqliteDb.Bounds.Add(new Bounds() { EastBound = neCorner.Decode().EastLongitude, NorthBound = neCorner.Decode().NorthLatitude, SouthBound = swCorner.Decode().SouthLatitude, WestBound = swCorner.Decode().WestLongitude, commonCodeLetters = commonStart, BestIdleCompletionTime = 0, LastPlayedOn = 0, StartedCurrentIdleRun = 0 });
            sqliteDb.IdleStats.Add(new IdleStats() { emptySpacePerSecond = 0, emptySpaceTotal = 0, graveyardSpacePerSecond = 0, graveyardSpaceTotal = 0, natureReserveSpacePerSecond = 0, natureReserveSpaceTotal = 0, parkSpacePerSecond = 0, parkSpaceTotal = 0, touristSpacePerSecond = 0, touristSpaceTotal = 0, trailSpacePerSecond = 0, trailSpaceTotal = 0 });
            sqliteDb.SaveChanges();

            //now we have the list of places we need to be concerned with. 
            Directory.CreateDirectory(relationID + "Tiles");
            Standalone.DrawMapTilesStandalone(relationID, buffered, allPlaces, saveToFolder);
            sqliteDb.SaveChanges();
            Log.WriteLog("Maptiles drawn at " + DateTime.Now);

            //Copy the files as necessary to their correct location.
            //if (saveToFolder)
                //Directory.Move(relationID + "Tiles", config["Solar2dExportFolder"] + "Tiles");

            //File.Copy(relationID + ".sqlite", config["Solar2dExportFolder"] + "database.sqlite");

            Log.WriteLog("Standalone gameplay DB done.");
        }
    }
}
