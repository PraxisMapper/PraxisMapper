using PraxisCore.Support;
using Google.OpenLocationCode;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using static PraxisCore.DbTables;
using static PraxisCore.StandaloneDbTables;
using static PraxisCore.ConstantValues;
using System.Collections.Concurrent;

namespace PraxisCore.Standalone
{
    //Stuff related to make a standalone, separate DB for offline games.
    public static class Standalone
    {
        /// <summary>
        /// Convert main DB OSM elements into compact estimated offline DB place info records.
        /// </summary>
        /// <param name="allPlaces">the list of elements to copy into the standalone DB</param>
        /// <returns>a list of PlaceInfo items to use in the standalone DB</returns>
        public static List<PlaceInfo2> GetPlaceInfo(List<DbTables.Place> allPlaces)
        {
            var results = new List<PlaceInfo2>();
            foreach (var place in allPlaces) //.Where(p => p.IsGameElement))
            {
                var center = place.ElementGeometry.Centroid.Coordinate;
                //The less symmetrical an area's envelope is, the less accurate this guess is. But that's the tradeoff i'm making
                //to get this all self-contained in the least amount of space. / 4 because (/2 for average, then /2 for radius instead of diameter)
                //Circular radius was replaced with square envelope. it's 1 extra double to store per row to do the envelope check this way, and looks more reasonable.
                //var calcRadius = (place.elementGeometry.EnvelopeInternal.Width + place.elementGeometry.EnvelopeInternal.Height) / 4;
                var pi = new PlaceInfo2() {
                    Name = TagParser.GetPlaceName(place.Tags),
                    areaType = place.GameElementName,
                    latCenter = center.Y,
                    lonCenter = center.X,
                    height = place.ElementGeometry.EnvelopeInternal.Height,
                    width = place.ElementGeometry.EnvelopeInternal.Width,
                    OsmElementId = place.SourceItemID };

                //Make points 1 Cell10 in size, so they're detectable.
                if (pi.height == 0) pi.height = .000125;
                if (pi.width == 0) pi.width = .000125;
                results.Add(pi);
            }

            return results;
        }

        /// <summary>
        /// Auto-create some scavenger hunt entries for a standalone game DB. Makes a list for entries that have a wikipedia tag,
        /// and then entries for all styles in the first TagParser style that have IsGameElement set to true.
        /// </summary>
        /// <param name="allPlaces">the list of OSM elements to search</param>
        /// <returns>the list of scavenger hunt entries</returns>
        public static List<ScavengerHuntStandalone> GetScavengerHunts(List<DbTables.Place> allPlaces)
        {
            //TODO: This is going to need updated for the new TagParser rules. testing at the minimum
            var results = new List<ScavengerHuntStandalone>();
            var wikiList = allPlaces.Where(a => a.Tags.Any(t => t.Key == "wikipedia") && TagParser.GetPlaceName(a.Tags) != "").Select(a => TagParser.GetPlaceName(a.Tags)).Distinct().ToList();
            //Create automatic scavenger hunt entries.
            Dictionary<string, List<string>> scavengerHunts = new Dictionary<string, List<string>>();

            //NOTE:
            //If i run this by elementID, i get everything unique but several entries get duplicated becaues they're in multiple pieces.
            //If I run this by name, the lists are much shorter but visiting one distinct location might count for all of them (This is a bigger concern with very large areas or retail establishment)
            //So I'm going to run this by name for the player's sake. 
            scavengerHunts.Add("Wikipedia Places", wikiList);
            Log.WriteLog(wikiList.Count() + " Wikipedia-linked items found for scavenger hunt.");
            //fill in gameElement lists.
            foreach (var gameElementTags in TagParser.allStyleGroups.First().Value.Where(s => s.Value.IsGameElement))
            {
                var foundElements = allPlaces.Where(a => TagParser.MatchOnTags(gameElementTags.Value, a.Tags) && !string.IsNullOrWhiteSpace(TagParser.GetPlaceName(a.Tags))).Select(a => TagParser.GetPlaceName(a.Tags)).Distinct().ToList();
                scavengerHunts.Add(gameElementTags.Value.Name, foundElements);
                Log.WriteLog(foundElements.Count() + " " + gameElementTags.Value.Name + " items found for scavenger hunt.");
            }

            foreach (var hunt in scavengerHunts)
            {
                foreach (var item in hunt.Value)
                    results.Add(new ScavengerHuntStandalone() { listName = hunt.Key, description = item, playerHasVisited = false });
            }
            return results;
        }

        /// <summary>
        /// Create the map tiles for an offline game, to be stored on the device alongside the database.
        /// </summary>
        /// <param name="relationID">the OSM relation the game will be built around</param>
        /// <param name="buffered">the GeoArea to use as the gameplay area</param>
        /// <param name="allPlaces">List of OSM Elements to use while drawing tiles</param>
        /// <param name="saveToFolder">If true, save images to a named folder. Does not currently save any output if this is false.</param>
        public static void DrawMapTilesStandalone(long relationID, GeoArea buffered, List<DbTables.Place> allPlaces, bool saveToFolder)
        {

            var intersectCheck = Converters.GeoAreaToPolygon(buffered);
            //start drawing maptiles and sorting out data.
            var swCorner = new OpenLocationCode(intersectCheck.EnvelopeInternal.MinY, intersectCheck.EnvelopeInternal.MinX);
            var neCorner = new OpenLocationCode(intersectCheck.EnvelopeInternal.MaxY, intersectCheck.EnvelopeInternal.MaxX);

            //declare how many map tiles will be drawn
            var xTiles = buffered.LongitudeWidth / resolutionCell8;
            var yTiles = buffered.LatitudeHeight / resolutionCell8;
            var totalTiles = Math.Truncate(xTiles * yTiles);

            Log.WriteLog("Starting processing maptiles for " + totalTiles + " Cell8 areas.");
            long mapTileCounter = 0;
            System.Diagnostics.Stopwatch progressTimer = new System.Diagnostics.Stopwatch();
            progressTimer.Start();

            //now, for every Cell8 involved, draw and name it.
            //This is tricky to run in parallel because it's not smooth increments
            var yCoords = new List<double>();
            var yVal = swCorner.Decode().SouthLatitude;
            while (yVal <= neCorner.Decode().NorthLatitude)
            {
                yCoords.Add(yVal);
                yVal += resolutionCell8;
            }

            var xCoords = new List<double>();
            var xVal = swCorner.Decode().WestLongitude;
            while (xVal <= neCorner.Decode().EastLongitude)
            {
                xCoords.Add(xVal);
                xVal += resolutionCell8;
            }
            System.Threading.ReaderWriterLockSlim dbLock = new System.Threading.ReaderWriterLockSlim();


            foreach (var y in yCoords)
            {
                //Make a collision box for just this row of Cell8s, and send the loop below just the list of things that might be relevant.
                //Add a Cell8 buffer space so all elements are loaded and drawn without needing to loop through the entire area.
                GeoArea thisRow = new GeoArea(y - ConstantValues.resolutionCell8, xCoords.First() - ConstantValues.resolutionCell8, y + ConstantValues.resolutionCell8 + ConstantValues.resolutionCell8, xCoords.Last() + resolutionCell8);
                var row = Converters.GeoAreaToPolygon(thisRow); 
                var rowList = allPlaces.Where(a => row.Intersects(a.ElementGeometry)).ToList();

                Parallel.ForEach(xCoords, x =>
                //foreach (var x in xCoords)
                {
                    //make map tile.
                    var plusCode = new OpenLocationCode(y, x, 10);
                    var plusCode8 = plusCode.CodeDigits.Substring(0, 8);
                    var plusCodeArea = OpenLocationCode.DecodeValid(plusCode8);

                    var areaForTile = new GeoArea(new GeoPoint(plusCodeArea.SouthLatitude, plusCodeArea.WestLongitude), new GeoPoint(plusCodeArea.NorthLatitude, plusCodeArea.EastLongitude));
                    var acheck = Converters.GeoAreaToPolygon(areaForTile); //this is faster than using a PreparedPolygon in testing, which was unexpected.
                    var areaList = rowList.Where(a => acheck.Intersects(a.ElementGeometry)).ToList(); //This one is for the maptile

                    //Create the maptile first, so if we save it to the DB/a file we can call the lock once per loop.
                    int imgX = 0, imgY = 0;
                    MapTileSupport.GetPlusCodeImagePixelSize(plusCode8, out imgX, out imgY);
                    var info = new ImageStats(areaForTile, imgX, imgY); //Each pixel is a Cell11, we're drawing a Cell8. For Cell6 testing this is 1600x2000, just barely within android limits
                    byte[] tile = null; // MapTiles.DrawAreaAtSize(info, areaList);
                    if (tile == null)
                    {
                        Log.WriteLog("Tile at " + x + "," + y + "Failed to draw!");
                        return;
                    }
                    if (saveToFolder) //some apps, like my Solar2D apps, can't use the byte[] in a DB row and need files.
                    {
                        //This split helps (but does not alleviate) Solar2D performance.
                        //A county-sized app will function this way, though sometimes when zoomed out it will not load all map tiles on an android device.
                        Directory.CreateDirectory(relationID + "Tiles\\" + plusCode8.Substring(0, 6));
                        System.IO.File.WriteAllBytes(relationID + "Tiles\\" + plusCode8.Substring(0, 6) + "\\" + plusCode8.Substring(6, 2) + ".pngTile", tile); //Solar2d also can't load pngs directly from an apk file in android, but the rule is extension based.
                    }

                    mapTileCounter++;
                    //if (progressTimer.ElapsedMilliseconds > 15000)
                    //{
                        
                    //    progressTimer.Restart();
                    //}
                });
                Log.WriteLog(mapTileCounter + " tiles processed, " + Math.Round((mapTileCounter / totalTiles) * 100, 2) + "% complete");

            }//);
        }

        /// <summary>
        /// Create an index table for the standalone offline game db. Takes everything inside a 6 digit PlusCode area, and save a list of elements inside it.
        /// </summary>
        /// <param name="buffered">the GeoArea to generate this indexed lookup for</param>
        /// <param name="allPlaces">the elements to search while making the index</param>
        /// <returns>a Dictionary of 6 digit PlusCodes and the list of OSM elements that intersect it. </returns>
        public static ConcurrentDictionary<string, List<DbTables.Place>> IndexAreasPerCell6(GeoArea buffered, List<DbTables.Place> allPlaces)
        {
            //NOTE: this could use the same optimization I applied to drawing map tiles
            var intersectCheck = Converters.GeoAreaToPolygon(buffered);
            //start drawing maptiles and sorting out data.
            var swCorner = new OpenLocationCode(intersectCheck.EnvelopeInternal.MinY, intersectCheck.EnvelopeInternal.MinX);
            var neCorner = new OpenLocationCode(intersectCheck.EnvelopeInternal.MaxY, intersectCheck.EnvelopeInternal.MaxX);

            //declare how many map tiles will be checked
            var xTiles = buffered.LongitudeWidth / resolutionCell6;
            var yTiles = buffered.LatitudeHeight / resolutionCell6;
            var totalTiles = Math.Truncate(xTiles * yTiles);

            var yCoords = new List<double>();
            var yVal = swCorner.Decode().SouthLatitude;
            while (yVal <= neCorner.Decode().NorthLatitude)
            {
                yCoords.Add(yVal);
                yVal += resolutionCell6;
            }

            var xCoords = new List<double>();
            var xVal = swCorner.Decode().WestLongitude;
            while (xVal <= neCorner.Decode().EastLongitude)
            {
                xCoords.Add(xVal);
                xVal += resolutionCell6;
            }

            ConcurrentDictionary<string, List<DbTables.Place>> results = new ConcurrentDictionary<string, List<DbTables.Place>>();

            foreach (var y in yCoords)
            {
                //Make a collision box for just this row of Cell8s, and send the loop below just the list of things that might be relevant.
                //Add a Cell8 buffer space so all elements are loaded and drawn without needing to loop through the entire area.
                GeoArea thisRow = new GeoArea(y - ConstantValues.resolutionCell8, xCoords.First() - ConstantValues.resolutionCell8, y + ConstantValues.resolutionCell8 + ConstantValues.resolutionCell8, xCoords.Last() + resolutionCell8);
                var row = Converters.GeoAreaToPolygon(thisRow);
                var rowList = allPlaces.Where(a => row.Intersects(a.ElementGeometry)).ToList();

                Parallel.ForEach(xCoords, x =>
                //foreach (var x in xCoords)
                {
                    var plusCode = new OpenLocationCode(y, x, 10);
                    var plusCode6 = plusCode.CodeDigits.Substring(0, 6);
                    var plusCodeArea = OpenLocationCode.DecodeValid(plusCode6);

                    var areaForTile = new GeoArea(new GeoPoint(plusCodeArea.SouthLatitude, plusCodeArea.WestLongitude), new GeoPoint(plusCodeArea.NorthLatitude, plusCodeArea.EastLongitude));
                    var acheck = Converters.GeoAreaToPolygon(areaForTile); //this is faster than using a PreparedPolygon in testing, which was unexpected.
                    var areaList = rowList.Where(a => acheck.Intersects(a.ElementGeometry)).ToList(); 

                    results.TryAdd(plusCode6, areaList);
                });
            }

            return results;

        }
    }
}