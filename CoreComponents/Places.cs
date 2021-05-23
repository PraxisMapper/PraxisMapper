using Google.OpenLocationCode;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;
using System.Linq;
using static CoreComponents.ConstantValues;
using static CoreComponents.DbTables;
using static CoreComponents.GeometrySupport;
using static CoreComponents.Singletons;

namespace CoreComponents
{
    public static class Place 
    {
        //for now, anything that does a query on MapData or a list of MapData entries
        //(To become a place for anything that works with the V4 data set)
        //Places will be the name for interactible or important areas on the map. Was not previously a fixed name for that.

        //This class is pending a rework to the newest storage logic. 
        //All elements in the table with Geometry will be valid, and the TagParser rules will determine which ones are game elements
        //this allows it to be customized much easier, and changed on the fly without reloading data.
        //A lot of the current code will need changed to match that new logic, though. And generated areas may remain separate.
        //public static List<MapData> GetPlacesMapDAta(GeoArea area, List<MapData> source = null, bool includeAdmin = false, bool includeGenerated= true, double filterSize = 0)
        //{
        //    //The flexible core of the lookup functions. Takes an area, returns results that intersect from Source. If source is null, looks into the DB.
        //    //Intersects is the only indexable function on a geography column I would want here. Distance and Equals can also use the index, but I don't need those in this app.
        //    List<MapData> places;
        //    if (source == null)
        //    {
        //        var location = Converters.GeoAreaToPolygon(area); //Prepared items don't work on a DB lookup.
        //        var db = new CoreComponents.PraxisContext();
        //        if (includeAdmin)
        //            places = db.MapData.Where(md => location.Intersects(md.place) && md.AreaSize > filterSize).ToList();
        //        else
        //            places = db.MapData.Where(md => md.AreaTypeId != 13 && location.Intersects(md.place) && md.AreaSize > filterSize).ToList();
        //        //TODO: make including generated areas a toggle? or assume that this call is trivial performance-wise on an empty table
        //        //A toggle might be good since this also affects maptiles
        //        if (includeGenerated) 
        //            places.AddRange(db.GeneratedMapData.Where(md => location.Intersects(md.place)).Select(g => new MapData() { MapDataId = g.GeneratedMapDataId + 100000000, place = g.place, type = g.type, name = g.name, AreaTypeId = g.AreaTypeId }));
        //    }
        //    else
        //    {
        //        var location = Converters.GeoAreaToPreparedPolygon(area);
        //        if (includeAdmin)
        //            places = source.Where(md => location.Intersects(md.place) && md.AreaSize > filterSize).Select(md => md.Clone()).ToList();
        //        else
        //            places = source.Where(md => md.AreaTypeId != 13 && location.Intersects(md.place) && md.AreaSize > filterSize).Select(md => md.Clone()).ToList();
        //    }
        //    return places;
        //}

        public static List<StoredWay> GetPlaces(GeoArea area, List<StoredWay> source = null, double filterSize = 0, List<TagParserEntry> styles = null)
        {

            if (styles == null)
                styles = TagParser.styles;
            //parameters i will need to restore later.
            //bool includeAdmin = false; this is no longer a hard-coded set. This is now a TagParser thing.
            bool includeGenerated = false;

            //The flexible core of the lookup functions. Takes an area, returns results that intersect from Source. If source is null, looks into the DB.
            //Intersects is the only indexable function on a geography column I would want here. Distance and Equals can also use the index, but I don't need those in this app.
            List<StoredWay> places;
            if (source == null)
            {
                var location = Converters.GeoAreaToPolygon(area); //Prepared items don't work on a DB lookup.
                var db = new CoreComponents.PraxisContext();
                    places = db.StoredWays.Include(s => s.WayTags).Where(md => location.Intersects(md.wayGeometry)).OrderByDescending(w => w.wayGeometry.Area).ThenByDescending(w => w.wayGeometry.Length).ToList(); // && md.AreaSize > filterSize
                //TODO: make including generated areas a toggle? or assume that this call is trivial performance-wise on an empty table
                //A toggle might be good since this also affects maptiles
                //if (includeGenerated)
                    //places.AddRange(db.StoredWays.Where(md => location.Intersects(md.wayGeometry)).Select(g => new MapData() { MapDataId = g.GeneratedMapDataId + 100000000, place = g.place, type = g.type, name = g.name, AreaTypeId = g.AreaTypeId }));
            }
            else
            {
                var location = Converters.GeoAreaToPreparedPolygon(area);
                places = source.Where(md => location.Intersects(md.wayGeometry)).Select(md => md.Clone()).ToList(); // && md.AreaSize > filterSize
            }
            return places;
        }

        //public static List<MapData> GetGeneratedPlaces(GeoArea area, List<MapData> source)
        //{
        //    //The flexible core of the lookup functions. Takes an area, returns results that intersect from Source. If source is null, looks into the DB.
        //    //Intersects is the only indexable function on a geography column I would want here. Distance and Equals can also use the index, but I don't need those in this app.
        //    List<MapData> places = new List<MapData>();
        //    if (source == null)
        //    {
        //        var db = new CoreComponents.PraxisContext();
        //        var location = Converters.GeoAreaToPolygon(area); //Prepared items don't work on a DB lookup.
        //        places.AddRange(db.GeneratedMapData.Where(md => location.Intersects(md.place)).Select(g => new MapData() { MapDataId = g.GeneratedMapDataId + 100000000, place = g.place, type = g.type, name = g.name, AreaTypeId = g.AreaTypeId }));
        //    }
        //    return places;
        //}

        public static List<StoredWay> GetGeneratedPlaces(GeoArea area)
        {
            //TODO: work out how I'm going to store generated places in the new schema.
            return null;
            
            //List<MapData> places = new List<MapData>();
            //if (source == null)
            //{
            //    var db = new CoreComponents.PraxisContext();
            //    var location = Converters.GeoAreaToPolygon(area); //Prepared items don't work on a DB lookup.
            //    places.AddRange(db.GeneratedMapData.Where(md => location.Intersects(md.place)).Select(g => new MapData() { MapDataId = g.GeneratedMapDataId + 100000000, place = g.place, type = g.type, name = g.name, AreaTypeId = g.AreaTypeId }));
            //}
            //return places;
        }

        public static List<StoredWay> GetAdminBoundaries(GeoArea area, List<StoredWay> source = null)
        {
            //Another function that's replaced by TagParser rules.
            return null;

            //List<StoredWay> places = new List<StoredWay>();
            //if (source == null)
            //{
            //    var db = new CoreComponents.PraxisContext();
            //    var location = Converters.GeoAreaToPolygon(area);
            //    var asAdminBounds = db.AdminBounds.Where(md => location.Intersects(md.place)).ToList();
            //    places = asAdminBounds.Select(m => (MapData)m).ToList();
            //}
            //return places;
        }

        //public static List<MapData> GetAdminBoundariesOld(GeoArea area, List<MapData> source = null)
        //{
        //    //If you ONLY want to get admin boundaries, use this function. Using GetPlaces searches for all place types, and is very slow by comparison.
        //    List<MapData> places = new List<MapData>();
        //    if (source == null)
        //    {
        //        var db = new CoreComponents.PraxisContext();
        //        var location = Converters.GeoAreaToPolygon(area);
        //        var asAdminBounds =  db.AdminBounds.Where(md => location.Intersects(md.place)).ToList();
        //        places = asAdminBounds.Select(m => (MapData)m).ToList();
        //    }
        //    return places;
        //}

        //Note: This should have the padding added to area before this is called, if checking for tiles that need regenerated.
        public static bool DoPlacesExist(GeoArea area, List<StoredWay> source = null)
        {
            //As GetPlaces, but only checks if there are entries. This also currently skipss admin boundaries as well for determining if stuff 'exists', since those aren't drawn.
            bool includeGenerated = false; //parameter to readd later
            var location = Converters.GeoAreaToPolygon(area);
            bool places;
            if (source == null)
            {
                var db = new PraxisContext();
                places = db.StoredWays.Any(md => md.wayGeometry.Intersects(location));
                if (includeGenerated)
                    places = places; // || db.GeneratedMapData.Any(md => md.place.Intersects(location));
                return places;
            }
            else
            {
                places = source.Any(md => md.wayGeometry.Intersects(location));
            }
            return places;
        }

        public static List<GeneratedMapData> CreateInterestingPlaces(string plusCode, bool autoSave = true)
        {
            //expected to receive a Cell8
            // populate it with some interesting regions for players.
            Random r = new Random();
            CodeArea cell8 = OpenLocationCode.DecodeValid(plusCode); //Reminder: resolution is .0025 on a Cell8
            int shapeCount = 1; // 2; //number of shapes to apply to the Cell8
            double shapeWarp = .3; //percentage a shape is allowed to have each vertexs drift by.
            List<GeneratedMapData> areasToAdd = new List<GeneratedMapData>();

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
                    GeneratedMapData gmd = new GeneratedMapData();
                    //gmd.AreaTypeId = 100; //a fixed type for when we want to treat generated areas differently than fixed, real world areas.
                    //gmd.AreaTypeId = r.Next(1, 13); //Randomly assign this area an interesting area type, for games that want one.
                    gmd.name = ""; //not using this on this level. 
                    gmd.place = polygon;
                    gmd.type = "generated";
                    gmd.GeneratedAt = DateTime.Now;
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
                    area.place = CCWCheck((Polygon)area.place); //fixes errors that reappeared above
                }
                db.GeneratedMapData.AddRange(areasToAdd);
                db.SaveChanges();
            }

            return areasToAdd;
        }

        public static string LoadDataOnPlace(long id)
        {
            //Debugging helper call. Loads up some information on an area and display it.
            var db = new PraxisContext();
            var entries = db.StoredWays.Where(m => m.id == id || m.sourceItemID == id).ToList();
            string results = "";
            foreach (var entry in entries)
            {
                var shape = entry.wayGeometry;

                results += "Name: " + entry.name + Environment.NewLine;
                results += "Game Type: " + TagParser.GetAreaType(entry.WayTags.ToList()) + Environment.NewLine;
                results += "Geometry Type: " + shape.GeometryType + Environment.NewLine;
                results += "OSM Type: " + entry.sourceItemType + Environment.NewLine;
                results += "Area Tags: " + String.Join(",", entry.WayTags.Select(t => t.Key + ":" + t.Value));
                results += "IsValid? : " + shape.IsValid + Environment.NewLine;
                results += "Area: " + shape.Area + Environment.NewLine; //Not documented, but I believe this is the area in square degrees. Is that a real unit?
                results += "As Text: " + shape.AsText() + Environment.NewLine;
            }

            return results;
        }

        public static GeoArea GetServerBounds(double resolution)
        {
            //Auto-detect what the boundaries are for the database's data set. This might be better off as a Larry command to calculate, and save the results in a DB table.
            //NOTE: with the Aleutian islands, the USA is considered as wide as the entire map. These sit on both sides of the meridian.
            //Detect which Cell2s are in play.
            //List<OpenLocationCode> placesToScan = new List<OpenLocationCode>();
            //These 2 start in the opposite corners, to make sure the replacements are correctly detected.
            double SouthLimit = 360; 
            double NorthLimit = -360;
            double WestLimit = 360;
            double EastLimit = -360;

            //OK, a better idea might be just to scan the world map with a Cell8-sized box that reaches the whole way across, and stop when you hit HasPlaces.
            //This could be optimized by scanning bigger plus codes down to smaller ones, but this gets it done.
            var northscanner = new GeoArea(new GeoPoint(90 - resolution, -180), new GeoPoint(90, 180));
            while(!DoPlacesExist(northscanner))
            {
                northscanner = new GeoArea(new GeoPoint(northscanner.SouthLatitude - resolution, -180), new GeoPoint(northscanner.NorthLatitude - resolution, 180));
            }
            NorthLimit = northscanner.NorthLatitude;

            var southscanner = new GeoArea(new GeoPoint(-90, -180), new GeoPoint(-90 + resolution, 180));
            while (!DoPlacesExist(southscanner))
            {
                southscanner = new GeoArea(new GeoPoint(southscanner.SouthLatitude + resolution, -180), new GeoPoint(southscanner.NorthLatitude + resolution, 180));
            }
            SouthLimit = southscanner.SouthLatitude;

            var westScanner = new GeoArea(new GeoPoint(-90, -180), new GeoPoint(90, -180 + resolution));
            while (!DoPlacesExist(westScanner))
            {
                westScanner = new GeoArea(new GeoPoint(-90, westScanner.WestLongitude + resolution), new GeoPoint(90, westScanner.EastLongitude + resolution));
            }
            WestLimit = westScanner.WestLongitude;

            var eastscanner = new GeoArea(new GeoPoint(-90, 180 - resolutionCell8), new GeoPoint(90, 180));
            while (!DoPlacesExist(eastscanner))
            {
                eastscanner = new GeoArea(new GeoPoint(-90, eastscanner.WestLongitude - resolution), new GeoPoint(90, eastscanner.EastLongitude - resolution));
            }
            EastLimit = eastscanner.EastLongitude;

            return new GeoArea(new GeoPoint(SouthLimit, WestLimit), new GeoPoint(NorthLimit, EastLimit));
        }

        public static bool IsInBounds(OpenLocationCode code, ServerSetting bounds)
        {
            //TODO: Bounds should loaded from the database at startup
            //right now, they're saved to a cache after the first actual webpage call. 
            var db = new PraxisContext();
            var area = code.Decode();

            return (bounds.NorthBound >= area.NorthLatitude && bounds.SouthBound <= area.SouthLatitude
                && bounds.EastBound >= area.EastLongitude && bounds.WestBound <= area.WestLongitude);
        }

        public static bool IsInBounds(string code, ServerSetting bounds)
        {
            if (bounds == null) //shouldn't happen, sanity check.
                return true; 

            var area = new OpenLocationCode(code); //Might need to re-add the + if its not present?
            return IsInBounds(area, bounds);
        }
    }
}
