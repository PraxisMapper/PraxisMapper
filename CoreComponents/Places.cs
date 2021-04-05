using Google.OpenLocationCode;
using NetTopologySuite.Geometries;
using OsmSharp.Tags;
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
        //Places will be the name for interactible or important areas on the map. Was not previously a fixed name for that.
        public static List<MapData> GetPlaces(GeoArea area, List<MapData> source = null, bool includeAdmin = false, bool includeGenerated= true, double filterSize = 0)
        {
            //The flexible core of the lookup functions. Takes an area, returns results that intersect from Source. If source is null, looks into the DB.
            //Intersects is the only indexable function on a geography column I would want here. Distance and Equals can also use the index, but I don't need those in this app.
            List<MapData> places;
            if (source == null)
            {
                var location = Converters.GeoAreaToPolygon(area); //Prepared items don't work on a DB lookup.
                var db = new CoreComponents.PraxisContext();
                if (includeAdmin)
                    places = db.MapData.Where(md => location.Intersects(md.place) && md.AreaSize > filterSize).ToList();
                else
                    places = db.MapData.Where(md => md.AreaTypeId != 13 && location.Intersects(md.place) && md.AreaSize > filterSize).ToList();
                //TODO: make including generated areas a toggle? or assume that this call is trivial performance-wise on an empty table
                //A toggle might be good since this also affects maptiles
                if (includeGenerated) 
                    places.AddRange(db.GeneratedMapData.Where(md => location.Intersects(md.place)).Select(g => new MapData() { MapDataId = g.GeneratedMapDataId + 100000000, place = g.place, type = g.type, name = g.name, AreaTypeId = g.AreaTypeId }));
            }
            else
            {
                var location = Converters.GeoAreaToPreparedPolygon(area);
                if (includeAdmin)
                    places = source.Where(md => location.Intersects(md.place) && md.AreaSize > filterSize).Select(md => md.Clone()).ToList();
                else
                    places = source.Where(md => md.AreaTypeId != 13 && location.Intersects(md.place) && md.AreaSize > filterSize).Select(md => md.Clone()).ToList();
            }
            return places;
        }

        public static List<MapData> GetGeneratedPlaces(GeoArea area, List<MapData> source = null)
        {
            //The flexible core of the lookup functions. Takes an area, returns results that intersect from Source. If source is null, looks into the DB.
            //Intersects is the only indexable function on a geography column I would want here. Distance and Equals can also use the index, but I don't need those in this app.
            List<MapData> places = new List<MapData>();
            if (source == null)
            {
                var db = new CoreComponents.PraxisContext();
                var location = Converters.GeoAreaToPolygon(area); //Prepared items don't work on a DB lookup.
                places.AddRange(db.GeneratedMapData.Where(md => location.Intersects(md.place)).Select(g => new MapData() { MapDataId = g.GeneratedMapDataId + 100000000, place = g.place, type = g.type, name = g.name, AreaTypeId = g.AreaTypeId }));
            }
            return places;
        }


        public static List<MapData> GetAdminBoundaries(GeoArea area, List<MapData> source = null)
        {
            //If you ONLY want to get admin boundaries, use this function. Using GetPlaces searches for all place types, and is very slow by comparison.
            List<MapData> places = new List<MapData>();
            if (source == null)
            {
                var db = new CoreComponents.PraxisContext();
                var location = Converters.GeoAreaToPolygon(area);
                var asAdminBounds =  db.AdminBounds.Where(md => location.Intersects(md.place)).ToList();
                places = asAdminBounds.Select(m => (MapData)m).ToList();
            }
            return places;
        }

        //Note: This should have the padding added to area before this is called, if checking for tiles that need regenerated.
        public static bool DoPlacesExist(GeoArea area, List<MapData> source = null, bool includeGenerated = true)
        {
            //As GetPlaces, but only checks if there are entries. This also currently skipss admin boundaries as well for determining if stuff 'exists', since those aren't drawn.
            var location = Converters.GeoAreaToPolygon(area);
            bool places;
            if (source == null)
            {
                var db = new PraxisContext();
                places = db.MapData.Any(md => md.place.Intersects(location) && md.AreaTypeId != 13); //Ignore admin boundaries for this purpose.
                if (includeGenerated)
                    places = places || db.GeneratedMapData.Any(md => md.place.Intersects(location));
                return places;
            }
            else
            {
                places = source.Any(md => md.place.Intersects(location));
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
                    gmd.AreaTypeId = 100; //a fixed type for when we want to treat generated areas differently than fixed, real world areas.
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

        public static string GetPlaceName(TagsCollectionBase tagsO)
        {
            if (tagsO.Count() == 0)
                return "";
            var tags = tagsO.ToLookup(k => k.Key, v => v.Value);

            string name = tags["name"].FirstOrDefault();
            if (name == null || name == "")
                //some things have a Note rather than a Name. Use that as a backup.
                name = tags["note"].FirstOrDefault();
            if (name == null)
                return "";

            return name;
        }

        public static string GetPlaceType(TagsCollectionBase tagsO)
        {
            //This is how we will figure out which area a cell counts as now.
            //Should make sure all of these exist in the AreaTypes table I made.
            //REMEMBER: this list needs to match the same tags as the ones in GetWays(relations)FromPBF or else data looks weird.

            if (tagsO.Count() == 0)
                return ""; //Sanity check

            var tags = tagsO.ToLookup(k => k.Key, v => v.Value); //this might not be necessary, tagscollectionbase supports string keys, but its an IEnumerable, which isnt as fast as ILookup used here.
            //Entries are currently sorted by order of importance (EX: an area that would qualify as multiple gets the first one on the list)

            //Wetlands are granted priority over water, since they're likely to be tagged as both and wetland is more interesting.
            if (DbSettings.processWetland && tags["natural"].Any(v => v == "wetland"))
                return "wetland";

            //Water spaces should be displayed. Not sure if I want players to be in them for resources.
            if (DbSettings.processWater && tags["natural"].Any(v => v == "water") || tags["waterway"].Count() > 0)
                return "water";

            //Trail. Will likely show up in varying places for various reasons. Trying to limit this down to hiking trails like in parks and similar.
            //In my local park, i see both path and footway used (Footway is an unpaved trail, Path is a paved one)
            //highway=track is for tractors and other vehicles.  Don't include that.
            //highway=path is non-motor vehicle, and otherwise very generic. 5% of all Ways in OSM.
            //highway=footway is pedestrian traffic only, maybe including bikes. Mostly sidewalks, which I dont' want to include.
            //highway=bridleway is horse paths, maybe including pedestrians and bikes
            //highway=cycleway is for bikes, maybe including pedestrians.
            if (DbSettings.processTrail && tags["highway"].Any(v => relevantTrailValues.Contains(v)
                && !tags["footway"].Any(v => v == "sidewalk" || v == "crossing")))
                return "trail";

            //Parks are good.
            if (DbSettings.processPark && tags["leisure"].Any(v => v == "park"))
                return "park";

            //admin boundaries: identify what political entities you're in. Smaller numbers are bigger levels (countries), bigger numbers are smaller entries (states, counties, cities, neighborhoods)
            //This should be the lowest priority tag on a cell, since this is probably the least interesting piece of info you could know.
            //OSM Wiki has more info on which ones mean what and where they're used.
            if (DbSettings.processAdmin && tags["boundary"].Any(v => v == "administrative")) //Admin_level appears on other elements, including goverment-tagged stuff, capitals, etc.
            {
                string level = tags["admin_level"].FirstOrDefault();
                if (level != null)
                    return "admin" + level.ToInt();
                return "admin0"; //indicates relation wasn't tagged with a level.
            }

            //Cemetaries are ok. They don't seem to appreciate Pokemon Go, but they're a public space and often encourage activity in them (thats not PoGo)
            if (DbSettings.processCemetery && (tags["landuse"].Any(v => v == "cemetery")
                || tags["amenity"].Any(v => v == "grave_yard")))
                return "cemetery";

            //I have historical as a tag to save, but not necessarily sub-sets yet of whats interesting there. Take everything for now.
            //NOTE: the OSM tag (historic) doesn't match my name (historical)
            if (DbSettings.processHistorical && tags["historic"].Count() > 0)
                return "historical";

            //Nature Reserve. Should be included
            if (DbSettings.processNatureReserve && tags["leisure"].Any(v => v == "nature_reserve"))
                return "natureReserve";

            //Tourist locations that aren' specifically businesses (public art and educational stuff is good. Hotels aren't).
            if (DbSettings.processTourism && tags["tourism"].Any(v => relevantTourismValues.Contains(v)))
                return "tourism"; //TODO: create sub-values for tourism types?

            //Universities are good. Primary schools are not so good.  Don't include all education values.
            if (DbSettings.processUniversity && tags["amenity"].Any(v => v == "university" || v == "college"))
                return "university";

            //Beaches are good. Managed beaches are less good but I'll count those too.
            if (DbSettings.processBeach && tags["natural"].Any(v => v == "beach")
            || (tags["leisure"].Any(v => v == "beach_resort")))
                return "beach";

            //Generic shopping area is ok. I don't want to advertise businesses, but this is a common area type. Would prefer other tags apply if possible.
            //Landuse=retail has 200k entries. building=retail has 500k entries.
            //shop=* has about 5 million entries, mostly nodes.
            if (DbSettings.processRetail && (tags["landuse"].Any(v => v == "retail")
                || tags["building"].Any(v => v == "retail")
                || tags["shop"].Count() > 0)) // mall is a value of shop, so those are included here now.
                return "retail";

            //Roads will be present most of the time since -processEverything is the default. But are checked next to last in case they're also a more interesting type
            // such as a Historic road.
            if (DbSettings.processRoads && tags["highway"].Any(v => relevantRoadValues.Contains(v))
            && !tags["footway"].Any(v => v == "sidewalk" || v == "crossing"))
            return "road";

            //Amenities are a separate tag, so i want to pull them out separately from (and before) buildings
            //There are lots of amenities, though, and i need to figure out the list that applies here 
            //without interfering with other types of areas. 

            //buildings will matter, since -processEverything defaults on. Done last, again, in case it's a Historic building or something.
            //Mark abandoned/unused buildings?
            if (DbSettings.processBuildings && tags.Contains("building"))
                return "building";


            //Parking lots should get drawn too. Same color as roads ideally.
            if (DbSettings.processParking && tags["amenity"].Any(v => v == "parking"))
                return "parking";

            //Possibly of interest:
            //landuse:forest / landuse:orchard  / natural:wood
            //natural:sand may be of interest for desert area?
            //natural:spring / natural:hot_spring? might be pretty rare.
            //amenity:theatre for plays/music/etc (amenity:cinema is a movie theater)
            //Anything else seems un-interesting or irrelevant.

            return ""; //not a type we need to save right now.
        }

        public static string LoadDataOnPlace(long id)
        {
            //Debugging helper call. Loads up some information on an area and display it.
            var db = new PraxisContext();
            var entries = db.MapData.Where(m => m.WayId == id || m.RelationId == id || m.NodeId == id || m.MapDataId == id).ToList();
            string results = "";
            foreach (var entry in entries)
            {
                var shape = entry.place;

                results += "Name: " + entry.name + Environment.NewLine;
                results += "Game Type: " + entry.type + Environment.NewLine;
                results += "Geometry Type: " + shape.GeometryType + Environment.NewLine;
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
            //This could be optimized by scanning bigger plus codes down to smaller ones, but this gets u
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
            //TODO: ponder how to handle this without a DB call each time. This should probably be cached by the app, so i need to cache ServerSettings and pass it in from the parent app.
            var db = new PraxisContext();
            //var bounds = db.ServerSettings.FirstOrDefault();
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
