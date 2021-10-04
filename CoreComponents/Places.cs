using PraxisCore.Support;
using Google.OpenLocationCode;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;
using System.Linq;
using static PraxisCore.ConstantValues;
using static PraxisCore.DbTables;
using static PraxisCore.GeometrySupport;
using static PraxisCore.Singletons;

namespace PraxisCore
{
    /// <summary>
    /// Places are StoredOsmElements 
    /// </summary>
    public static class Place 
    {
        //for now, anything that does a query on StoredOsmElement or a list of StoredOsmElement entries
        //Places will be the name for interactible or important areas on the map. Was not previously a fixed name for that.

        //All elements in the table with Geometry will be valid, and the TagParser rules will determine which ones are game elements
        //this allows it to be customized much easier, and changed on the fly without reloading data.
        //A lot of the current code will need changed to match that new logic, though. And generated areas may remain separate.

        /// <summary>
        /// The core for pulling in locations from PraxisMapper. Can do a new search on an existing list of StoredOsmElement or pulls from the database if none is provided. Adds padding as set from config automatically.
        /// </summary>
        /// <param name="area">The GeoArea to intersect locations against, and include ones that do. </param>
        /// <param name="source">Null to load from the database, or a List of StoredOsmElements to narrow down</param>
        /// <param name="filterSize">Removes any areas with a length or perimeter over this value. Defaults to 0 to include everything.</param>
        /// <param name="styleSet">A TagParser style set to run the found locations through for identification.</param>
        /// <param name="skipTags">If true, skips over tagging elements. A performance boost when you have a List to narrow down already.</param>
        /// <param name="includePoints">If false, removes Points from the source before returning the results</param>
        /// <returns>A list of StoredOsmElements that intersect the area, have a perimter greater than or equal to filtersize.</returns>
        public static List<StoredOsmElement> GetPlaces(GeoArea area, List<StoredOsmElement> source = null, double filterSize = 0, string styleSet = "mapTiles", bool skipTags = false, bool includePoints = true)
        {
            //parameters i will need to restore later.
            //bool includeGenerated = false;

            //The flexible core of the lookup functions. Takes an area, returns results that intersect from Source. If source is null, looks into the DB.
            //Intersects is the only indexable function on a geography column I would want here. Distance and Equals can also use the index, but I don't need those in this app.
            List<StoredOsmElement> places;
            if (source == null)
            {
                var paddedArea = MapTiles.MakeBufferedGeoArea(area);
                var location = Converters.GeoAreaToPolygon(paddedArea); //Prepared items don't work on a DB lookup.
                var db = new PraxisCore.PraxisContext();
                db.Database.SetCommandTimeout(new TimeSpan(0, 5, 0));
                if (skipTags) //Should make the load slightly faster if we're parsing existing items that already got tags applied
                {
                    places = db.StoredOsmElements.Where(md => location.Intersects(md.elementGeometry) && md.AreaSize >= filterSize && (includePoints || md.sourceItemType != 1)).OrderByDescending(w => w.elementGeometry.Area).ThenByDescending(w => w.elementGeometry.Length).ToList();
                    return places; //Jump out before we do ApplyTags
                }
                else
                {
                    places = db.StoredOsmElements.Include(s => s.Tags).Where(md => location.Intersects(md.elementGeometry) && md.AreaSize >= filterSize && (includePoints || md.sourceItemType != 1)).OrderByDescending(w => w.elementGeometry.Area).ThenByDescending(w => w.elementGeometry.Length).ToList();
                }
            }
            else
            {
                var location = Converters.GeoAreaToPreparedPolygon(area);
                places = source.Where(md => location.Intersects(md.elementGeometry) && md.AreaSize >= filterSize && (includePoints || md.sourceItemType != 1)).Select(md => md.Clone()).ToList();
            }

            if (!skipTags)
                TagParser.ApplyTags(places, styleSet); //populates the fields we don't save to the DB.
            return places;
        }

        /// <summary>
        /// A shortcut function to pull GetPlaces info against an ImageStats objects.
        /// </summary>
        /// <param name="stats">the ImageStats containing some of the other parameters to pass through</param>
        /// <param name="source">Null to load from the database, or a List of StoredOsmElements to narrow down</param>
        /// <param name="styleSet">A TagParser style set to run the found locations through for identification.</param>
        /// <param name="skipTags">If true, skips over tagging elements. A performance boost when you have a List to narrow down already.</param>
        /// <returns>A list of StoredOsmElements that intersect the area, have a perimter greater than or equal to filtersize.</returns>
        public static List<StoredOsmElement> GetPlacesForTile(ImageStats stats, List<StoredOsmElement> source = null, string styleSet = "mapTiles", bool skipTags = false)
        {
            var dataLoadArea = new GeoArea(stats.area.SouthLatitude - ConstantValues.resolutionCell10, stats.area.WestLongitude - ConstantValues.resolutionCell10, stats.area.NorthLatitude + ConstantValues.resolutionCell10, stats.area.EastLongitude + ConstantValues.resolutionCell10);
            return GetPlaces(dataLoadArea, source, stats.filterSize, styleSet, skipTags, stats.drawPoints);
        }

        /// <summary>
        /// Currently not used. Returns a list of Places from GetPlaces against the given area that have the IsGenerated flag set.
        /// </summary>
        /// <param name="area">the GeoArea to load intersecting elements with</param>
        /// <returns>A list of StoredOsmElements that intersect the area and were generated by PraxisMapper instead of being loaded from OSM data.</returns>
        public static List<StoredOsmElement> GetGeneratedPlaces(GeoArea area)
        {
            //TODO: work out how I'm going to store generated places in the new schema.
            //Generated Places are now stored in the same geometry table, with a flag.
            var tempPlaces = GetPlaces(area);
            return tempPlaces.Where(p => p.IsGenerated).ToList();
        }

        //Note: This should have the padding added to area before this is called, if checking for tiles that need regenerated.
        /// <summary>
        /// Checks if places existing in the DB for the given area. Will almost always return true, given admin boundaries are loaded into a database. Used for finding server boundaries and if an area needs maptiles drawn.
        /// </summary>
        /// <param name="area">The area to check for elements</param>
        /// <param name="source">an optional list to use instead of loading from the database.</param>
        /// <returns>true if any StoredOsmElements intersect the given GeoArea, false if not.</returns>
        public static bool DoPlacesExist(GeoArea area, List<StoredOsmElement> source = null)
        {
            //As GetPlaces, but only checks if there are entries.
            bool includeGenerated = false; //parameter to readd later
            var location = Converters.GeoAreaToPolygon(area);
            bool places;
            if (source == null)
            {
                var db = new PraxisContext();
                places = db.StoredOsmElements.Any(md => md.elementGeometry.Intersects(location));
                //if (includeGenerated)
                    //places = places; // || db.GeneratedMapData.Any(md => md.place.Intersects(location));
                return places;
            }
            else
            {
                places = source.Any(md => md.elementGeometry.Intersects(location));
            }
            return places;
        }

        /// <summary>
        /// Auto-generate some places to be used as gameplay elements in otherwise sparse areas. Given an 8 digit PlusCode, creates and warps some standard shapes in the Cell.
        /// </summary>
        /// <param name="plusCode">The area to generate shape(s) in</param>
        /// <param name="autoSave">If true, saves the areas to the database immediately.</param>
        /// <returns>The list of places created for the given area.</returns>
        public static List<StoredOsmElement> CreateInterestingPlaces(string plusCode, bool autoSave = true)
        {
            //expected to receive a Cell8
            // populate it with some interesting regions for players.
            Random r = new Random();
            CodeArea cell8 = OpenLocationCode.DecodeValid(plusCode); //Reminder: area is .0025 degrees on a Cell8
            int shapeCount = 1; // 2; //number of shapes to apply to the Cell8
            double shapeWarp = .3; //percentage a shape is allowed to have each vertexs drift by.
            List<StoredOsmElement> areasToAdd = new List<StoredOsmElement>();

            for (int i = 0; i < shapeCount; i++)
            {
                //Pick a shape
                var masterShape = possibleShapes.OrderBy(s => r.Next()).First();
                var shapeToAdd = masterShape.Select(s => new Coordinate(s)).ToList();
                var scaleFactor = r.Next(10, 36) * .01; //Math.Clamp(r.Next, .1, .35); //Ensure that we get a value that isn't terribly useless. 2 shapes can still cover 70%+ of an empty area this way.
                var positionFactorX = r.NextDouble() * resolutionCell8;
                var positionFactorY = r.NextDouble() * resolutionCell8;
                foreach (Coordinate c in shapeToAdd)
                {
                    //scale it to our resolution
                    c.X *= resolutionCell8;
                    c.Y *= resolutionCell8;

                    //multiply this by some factor smaller than 1, so it doesn't take up the entire Cell
                    //If we use NextDouble() here, it scales each coordinate randomly, which would look very unpredictable. Use the results of one call twice to scale proportionally. 
                    //but ponder how good/bad it looks for various shapes if each coordinate is scaled differently.
                    c.X *= scaleFactor;
                    c.Y *= scaleFactor;

                    //Rotate the coordinate set some random number of degrees?
                    //TODO: how to rotate these?

                    //Place the shape somewhere randomly by adding the same X/Y value less than the resolution to each point
                    c.X += positionFactorX;
                    c.Y += positionFactorY;

                    //Fuzz each vertex by adding some random distance on each axis less than 30% of the cell's size in either direction.
                    //10% makes the shapes much more recognizable, but not as interesting. Will continue looking into parameters here to help adjust that.
                    c.X += (r.NextDouble() * resolutionCell8 * shapeWarp) * (r.Next() % 2 == 0 ? 1 : -1);
                    c.Y += (r.NextDouble() * resolutionCell8 * shapeWarp) * (r.Next() % 2 == 0 ? 1 : -1);

                    //Let us know if this shape overlaps a neighboring cell. We probably want to make sure we re-draw map tiles if it does.
                    if (c.X > .0025 || c.Y > .0025)
                        Log.WriteLog("Coordinate for shape " + i + " in Cell8 " + plusCode + " will be out of bounds: " + c.X + " " + c.Y, Log.VerbosityLevels.High);

                    //And now add the minimum values for the given Cell8 to finish up coordinates.
                    c.X += cell8.Min.Longitude;
                    c.Y += cell8.Min.Latitude;
                }

                //ShapeToAdd now has a randomized layout, convert it to a polygon.
                shapeToAdd.Add(shapeToAdd.First()); //make it a closed shape
                var polygon = factory.CreatePolygon(shapeToAdd.ToArray());
                polygon = CCWCheck(polygon); //Sometimes squares still aren't CCW? or this gets un-done somewhere later?
                if (!polygon.IsValid || !polygon.Shell.IsCCW)
                {
                    Log.WriteLog("Invalid geometry generated, retrying", Log.VerbosityLevels.High);
                    i--;
                    continue;
                }

                if (!polygon.CoveredBy(Converters.GeoAreaToPolygon(cell8)))
                {
                    //TODO: Erase an existing Cell8/Cell10 map tile and let it get replaced next call.
                    //This should only ever require checking the map tile north/east of the current one, even though the vertex fuzzing can potentially move things negative slightly.
                    Log.WriteLog("This polygon is outside of the Cell8 by " + (cell8.Max.Latitude - shapeToAdd.Max(s => s.Y)) + "/" + (cell8.Max.Longitude - shapeToAdd.Max(s => s.X)), Log.VerbosityLevels.High);
                }
                if (polygon != null)
                {
                    StoredOsmElement gmd = new StoredOsmElement();
                    gmd.name = ""; //not using this on this level. 
                    gmd.elementGeometry = polygon;
                    gmd.GameElementName = "generated";
                    gmd.IsGenerated = true;
                    areasToAdd.Add(gmd); //this is the line that makes some objects occasionally not be CCW that were CCW before. Maybe its the cast to the generic Geometry item?
                }
                else
                {
                    //Inform me that I did something wrong.
                    Log.WriteLog("failed to convert a randomized shape to a polygon.");
                    continue;
                }
            }

            //Making this function self-contained
            if (autoSave)
            {
                var db = new PraxisContext();
                foreach (var area in areasToAdd)
                {
                    area.elementGeometry = CCWCheck((Polygon)area.elementGeometry); //fixes errors that reappeared above
                }
                db.StoredOsmElements.AddRange(areasToAdd);
                db.SaveChanges();
            }

            return areasToAdd;
        }

        /// <summary>
        /// A debugging function, writres some information an element using its OSM id (or internal primary key) to load from the database.
        /// </summary>
        /// <param name="id">the StoredOsmElementId or SourceElementId of an area to load.</param>
        /// <returns>a string with some details on the area in question.</returns>
        public static string LoadDataOnPlace(long id)
        {
            //Debugging helper call. Loads up some information on an area and display it.
            //Not currently used anywhere.
            var db = new PraxisContext();
            var entries = db.StoredOsmElements.Where(m => m.id == id || m.sourceItemID == id).ToList();
            string results = "";
            foreach (var entry in entries)
            {
                var shape = entry.elementGeometry;

                results += "Name: " + entry.name + Environment.NewLine;
                results += "Game Type: " + TagParser.GetAreaType(entry.Tags.ToList()) + Environment.NewLine;
                results += "Geometry Type: " + shape.GeometryType + Environment.NewLine;
                results += "OSM Type: " + entry.sourceItemType + Environment.NewLine;
                results += "Area Tags: " + String.Join(",", entry.Tags.Select(t => t.Key + ":" + t.Value));
                results += "IsValid? : " + shape.IsValid + Environment.NewLine;
                results += "Area: " + shape.Area + Environment.NewLine; //Not documented, but I believe this is the area in square degrees. Is that a real unit?
                results += "As Text: " + shape.AsText() + Environment.NewLine;
            }

            return results;
        }

        /// <summary>
        /// Scans the database to determine the outer boundaries of locations in it.
        /// </summary>
        /// <param name="resolution">How many degrees to scan at a time. Must be smaller than 1.</param>
        /// <returns>a GeoArea representing the bounds of the server's location at the resolution given.</returns>
        public static GeoArea DetectServerBounds(double resolution)
        {
            //Auto-detect what the boundaries are for the database's data set.
            //NOTE: with the Aleutian islands, the USA is considered as wide as the entire map. These sit on both sides of the meridian.
            //These 2 start in the opposite corners, to make sure the replacements are correctly detected.
            double SouthLimit = 360; 
            double NorthLimit = -360;
            double WestLimit = 360;
            double EastLimit = -360;

            double scanRes = 1; //1 degree.
            //This is now a 2-step process for speed. The first pass runs at 1 degree intervals for speed, then drops to the given resolution for precision.
            var northscanner = new GeoArea(new GeoPoint(90 - scanRes, -180), new GeoPoint(90, 180));
            while(!DoPlacesExist(northscanner))
                northscanner = new GeoArea(new GeoPoint(northscanner.SouthLatitude - scanRes, -180), new GeoPoint(northscanner.NorthLatitude - scanRes, 180));
            northscanner = new GeoArea(new GeoPoint(northscanner.SouthLatitude - resolution, -180), new GeoPoint(northscanner.NorthLatitude - resolution, 180));
            while (!DoPlacesExist(northscanner))
                northscanner = new GeoArea(new GeoPoint(northscanner.SouthLatitude - resolution, -180), new GeoPoint(northscanner.NorthLatitude - resolution, 180));
            NorthLimit = northscanner.NorthLatitude;

            var southscanner = new GeoArea(new GeoPoint(-90, -180), new GeoPoint(-90 + scanRes, 180));
            while (!DoPlacesExist(southscanner))
                southscanner = new GeoArea(new GeoPoint(southscanner.SouthLatitude + scanRes, -180), new GeoPoint(southscanner.NorthLatitude + scanRes, 180));
            southscanner = new GeoArea(new GeoPoint(southscanner.SouthLatitude + resolution, -180), new GeoPoint(southscanner.NorthLatitude + resolution, 180));
            while (!DoPlacesExist(southscanner))
                southscanner = new GeoArea(new GeoPoint(southscanner.SouthLatitude + resolution, -180), new GeoPoint(southscanner.NorthLatitude + resolution, 180));
            SouthLimit = southscanner.SouthLatitude;

            var westScanner = new GeoArea(new GeoPoint(-90, -180), new GeoPoint(90, -180 + scanRes));
            while (!DoPlacesExist(westScanner))
                westScanner = new GeoArea(new GeoPoint(-90, westScanner.WestLongitude + scanRes), new GeoPoint(90, westScanner.EastLongitude + scanRes));
            westScanner = new GeoArea(new GeoPoint(-90, westScanner.WestLongitude + resolution), new GeoPoint(90, westScanner.EastLongitude + resolution));
            while (!DoPlacesExist(westScanner))
                westScanner = new GeoArea(new GeoPoint(-90, westScanner.WestLongitude + resolution), new GeoPoint(90, westScanner.EastLongitude + resolution));
            WestLimit = westScanner.WestLongitude;

            var eastscanner = new GeoArea(new GeoPoint(-90, 180 - scanRes), new GeoPoint(90, 180));
            while (!DoPlacesExist(eastscanner))
                eastscanner = new GeoArea(new GeoPoint(-90, eastscanner.WestLongitude - scanRes), new GeoPoint(90, eastscanner.EastLongitude - scanRes));
            eastscanner = new GeoArea(new GeoPoint(-90, eastscanner.WestLongitude - resolution), new GeoPoint(90, eastscanner.EastLongitude - resolution));
            while (!DoPlacesExist(eastscanner))
                eastscanner = new GeoArea(new GeoPoint(-90, eastscanner.WestLongitude - resolution), new GeoPoint(90, eastscanner.EastLongitude - resolution));
            EastLimit = eastscanner.EastLongitude;

            return new GeoArea(new GeoPoint(SouthLimit, WestLimit), new GeoPoint(NorthLimit, EastLimit));
        }
        
        //This is for getting all places that have a specific TagParser style/match. Admin boundaries, parks, etc.
        /// <summary>
        /// Pull in all elements from a list that have a given style. 
        /// </summary>
        /// <param name="type">the name of the style to select elements for</param>
        /// <param name="area">the GeoArea to select elements from.</param>
        /// <param name="places">A list of OSM Elements to search. If null, loaded from the database based on the area provided.</param>
        /// <returns>a list of OSM Elements with the requested style in the given area.</returns>
        public static List<StoredOsmElement> GetPlacesByStyle(string type, GeoArea area, List<StoredOsmElement> places = null)
        {
            if (places == null)
                places = GetPlaces(area);
            return places.Where(p => p.GameElementName == type).ToList();
        }
    }
}
