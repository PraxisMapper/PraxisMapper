using Google.OpenLocationCode;
using NetTopologySuite;
using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static DatabaseAccess.DbTables;
using OsmSharp;
using DatabaseAccess.Support;
using OsmSharp.Tags;
using NetTopologySuite.Operation.Buffer;
using Microsoft.EntityFrameworkCore;
using System.Net;
using NetTopologySuite.Simplify;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing.Processing;

namespace DatabaseAccess
{
    public static class MapSupport
    {
        //TODO: define a real purpose and location for this stuff.
        //Right now, this is mostly 'functions/consts I want to refer to in multiple projects'

        //TODO:
        //set up a command line parameter for OsmXmlParser to extract certain types of value from files (so different users could pull different features out to use)
        //continue renmaing and reorganizing things.

        //the 11th digit uses a 5x4 grid, not a 20x20. They need separate scaling values for X and Y and are rectangular even at the equator.
        public const double resolution11Lat = .00003125;
        public const double resolution11Lon = .000025;
        public const double resolution10 = .000125; //the size of a 10-digit PlusCode, in degrees.
        public const double resolution8 = .0025; //the size of a 8-digit PlusCode, in degrees.
        public const double resolution6 = .05; //the size of a 6-digit PlusCode, in degrees.
        public const double resolution4 = 1; //the size of a 4-digit PlusCode, in degrees.
        public const double resolution2 = 20; //the size of a 2-digit PlusCode, in degrees.

        public static List<string> relevantTourismValues = new List<string>() { "artwork", "attraction", "gallery", "museum", "viewpoint", "zoo" }; //The stuff we care about in the tourism category. Zoo and attraction are debatable.
        public static List<string> relevantTrailValues = new List<string>() { "path", "bridleway", "cycleway", "footway", "living_street" }; //The stuff we care about in the highway category for trails. Living Streets are nonexistant in the US.
        public static List<string> relevantRoadValues = new List<string>() { "motorway", "trunk", "primary", "secondary", "tertiary", "unclassified", "residential", "motorway_link", "trunk_link", "primary_link", "secondary_link", "tertiary_link", "service", "road" }; //The stuff we care about in the highway category for roads. A lot more options for this.

        public static GeometryFactory factory = NtsGeometryServices.Instance.CreateGeometryFactory(new PrecisionModel(1000000), 4326); //SRID matches Plus code values.  Precision model means round all points to 7 decimal places to not exceed float's useful range.

        public static List<AreaType> areaTypes = new List<AreaType>() {
            //Areas here are for the original explore concept
            new AreaType() { AreaTypeId = 999, AreaName = "", OsmTags = "", HtmlColorCode = "545454"}, //the default background color. 0 causes insert to fail with an identity column
            new AreaType() { AreaTypeId = 1, AreaName = "water", OsmTags = "", HtmlColorCode = "0000B3"},
            new AreaType() { AreaTypeId = 2, AreaName = "wetland", OsmTags = "", HtmlColorCode = "0C4026"},
            new AreaType() { AreaTypeId = 3, AreaName = "park", OsmTags = "", HtmlColorCode = "00B300"},
            new AreaType() { AreaTypeId = 4, AreaName = "beach", OsmTags = "", HtmlColorCode = "D7B526" },
            new AreaType() { AreaTypeId = 5, AreaName = "university", OsmTags = "", HtmlColorCode = "F5F0DB" },
            new AreaType() { AreaTypeId = 6, AreaName = "natureReserve", OsmTags = "", HtmlColorCode = "124504" },
            new AreaType() { AreaTypeId = 7, AreaName = "cemetery", OsmTags = "", HtmlColorCode = "242420" },
            new AreaType() { AreaTypeId = 9, AreaName = "retail", OsmTags = "", HtmlColorCode = "EB63EB" },
            new AreaType() { AreaTypeId = 10, AreaName = "tourism", OsmTags = "", HtmlColorCode = "1999D1" },
            new AreaType() { AreaTypeId = 11, AreaName = "historical", OsmTags = "", HtmlColorCode = "B3B3B3" },
            new AreaType() { AreaTypeId = 12, AreaName = "trail", OsmTags = "", HtmlColorCode = "782E05" },
            new AreaType() { AreaTypeId = 13, AreaName = "admin", OsmTags = "",HtmlColorCode = "000000" },
            
            //These areas are for a more detailed, single area focused map game
            new AreaType() { AreaTypeId = 14, AreaName = "building", OsmTags = "", HtmlColorCode = "808080" },
            new AreaType() { AreaTypeId = 15, AreaName = "road", OsmTags = "", HtmlColorCode = "0D0D0D"},
            new AreaType() { AreaTypeId = 16, AreaName = "parking", OsmTags = "", HtmlColorCode = "0D0D0D" },
            //new AreaType() { AreaTypeId = 17, AreaName = "amenity", OsmTags = "", HtmlColorCode = "F2F090" }, //no idea what color this is
            //not yet completely certain i want to pull in amenities as their own thing. its sort of like retail but somehow more generic
            //maybe i need to add some more amenity entries to retail?
        };

        public static ILookup<string, int> areaTypeReference = areaTypes.ToLookup(k => k.AreaName, v => v.AreaTypeId);
        public static ILookup<int, string> areaIdReference = areaTypes.ToLookup(k => k.AreaTypeId, v => v.AreaName);
        public static ILookup<int, string> areaColorReference = areaTypes.ToLookup(k => k.AreaTypeId, v => v.HtmlColorCode);

        public static List<MapData> GetPlaces(GeoArea area, List<MapData> source = null)
        {
            //The flexible core of the lookup functions. Takes an area, returns results that intersect from Source. If source is null, looks into the DB.
            //Intersects is the only indexable function on a geography column I would want here. Distance and Equals can also use the index, but I don't need those in this app.
            var coordSeq = MakeBox(area);
            var location = factory.CreatePolygon(coordSeq);
            List<MapData> places;
            if (source == null)
            {
                var db = new DatabaseAccess.GpsExploreContext();
                places = db.MapData.Where(md => md.place.Intersects(location)).ToList();
            }
            else
                places = source.Where(md => md.place.Intersects(location)).ToList();
            return places;
        }

        public static Coordinate[] MakeBox(GeoArea plusCodeArea)
        {
            var factory = NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326);
            var cord1 = new Coordinate(plusCodeArea.Min.Longitude, plusCodeArea.Min.Latitude);
            var cord2 = new Coordinate(plusCodeArea.Min.Longitude, plusCodeArea.Max.Latitude);
            var cord3 = new Coordinate(plusCodeArea.Max.Longitude, plusCodeArea.Max.Latitude);
            var cord4 = new Coordinate(plusCodeArea.Max.Longitude, plusCodeArea.Min.Latitude);
            var cordSeq = new Coordinate[5] { cord4, cord3, cord2, cord1, cord4 };

            return cordSeq;
        }

        public static void SplitArea(GeoArea area, int divideCount, List<MapData> places, out List<MapData>[] placeArray, out GeoArea[] areaArray)
        {
            //Take area, divide it into a divideCount * divideCount grid of area. Return matching arrays of MapData and GeoArea, with indexes that correspond 1:1
            //The purpose of this function is to reduce code involved in optimizing a search, and to make it more flexible to test 
            //performance improvements on area splits.

            placeArray = new List<MapData>[1];
            areaArray = new GeoArea[1];

            if (divideCount == 0 || divideCount == 1)
            {
                placeArray[0] = places;
                areaArray[0] = area;
                return;
            }

            var latDivider = area.LatitudeHeight / divideCount;
            var lonDivider = area.LongitudeWidth / divideCount;

            List<List<MapData>> resultsPlace = new List<List<MapData>>();
            List<GeoArea> resultsArea = new List<GeoArea>();
            resultsArea.Capacity = divideCount * divideCount;
            resultsPlace.Capacity = divideCount * divideCount;

            for (var x = 0; x < divideCount; x++)
            {
                for (var y = 0; y < divideCount; y++)
                {
                    var box = new GeoArea(area.SouthLatitude + (latDivider * y),
                        area.WestLongitude + (lonDivider * x),
                        area.SouthLatitude + (latDivider * y) + latDivider,
                        area.WestLongitude + (lonDivider * x) + lonDivider);
                    resultsPlace.Add(GetPlaces(box, places));
                    resultsArea.Add(box);
                }
            }

            placeArray = resultsPlace.ToArray();
            areaArray = resultsArea.ToArray();

            return;
        }

        public static StringBuilder SearchArea(ref GeoArea area, ref List<MapData> mapData, bool entireCode = false)
        {
            StringBuilder sb = new StringBuilder();
            if (mapData.Count() == 0)
                return sb;

            var xCells = area.LongitudeWidth / resolution10;
            var yCells = area.LatitudeHeight / resolution10;

            for (double xx = 0; xx < xCells; xx += 1)
            {
                for (double yy = 0; yy < yCells; yy += 1)
                {
                    double x = area.Min.Longitude + (resolution10 * xx);
                    double y = area.Min.Latitude + (resolution10 * yy);

                    var placesFound = MapSupport.FindPlacesIn10Cell(x, y, ref mapData, entireCode);
                    if (!string.IsNullOrWhiteSpace(placesFound))
                        sb.AppendLine(placesFound);
                }
            }

            return sb;
        }

        public static string FindPlacesIn10Cell(double x, double y, ref List<MapData> places, bool entireCode = false)
        {
            var box = new GeoArea(new GeoPoint(y, x), new GeoPoint(y + resolution10, x + resolution10));
            var entriesHere = MapSupport.GetPlaces(box, places).Where(p => p.AreaTypeId !=13).ToList(); //Excluding admin boundaries from this list.  

            if (entriesHere.Count() == 0)
                return "";

            string area = DetermineAreaPoint(entriesHere);
            if (area != "")
            {
                string olc;
                if (entireCode)
                    olc = new OpenLocationCode(y, x).CodeDigits;
                else
                    olc = new OpenLocationCode(y, x).CodeDigits.Substring(6, 4); //This takes lat, long, Coordinate takes X, Y. This line is correct.
                return olc + "|" + area;
            }
            return "";
        }

        //As above, but only returns type ID
        public static int GetAreaTypeFor10Cell(double x, double y, ref List<MapData> places)
        {
            var box = new GeoArea(new GeoPoint(y, x), new GeoPoint(y + resolution10, x + resolution10));
            var entriesHere = MapSupport.GetPlaces(box, places).Where(p => p.AreaTypeId != 13).ToList(); //Excluding admin boundaries from this list.  

            if (entriesHere.Count() == 0)
                return 0;

            int area = DetermineAreaType(entriesHere);
            return area;
        }

        public static int GetAreaTypeFor11Cell(double x, double y, ref List<MapData> places)
        {
            var box = new GeoArea(new GeoPoint(y, x), new GeoPoint(y + resolution11Lat, x + resolution11Lon));
            var entriesHere = MapSupport.GetPlaces(box, places).Where(p => p.AreaTypeId != 13).ToList(); //Excluding admin boundaries from this list.  

            if (entriesHere.Count() == 0)
                return 0;

            int area = DetermineAreaType(entriesHere);
            return area;
        }



        //I don't think this is going to be a high-demand function, but I'll include it for performance comparisons or possibly low-res tiles.
        public static string FindPlacesIn11Cell(double x, double y, ref List<MapData> places, bool entireCode = false)
        {
            var box = new GeoArea(new GeoPoint(y, x), new GeoPoint(y + resolution11Lat, x + resolution11Lon));
            var entriesHere = MapSupport.GetPlaces(box, places).Where(p => p.AreaTypeId != 13).ToList(); //Excluding admin boundaries from this list.  

            if (entriesHere.Count() == 0)
                return "";

            string area = DetermineAreaPoint(entriesHere);
            if (area != "")
            {
                string olc;
                if (entireCode)
                    olc = new OpenLocationCode(y, x, 11).CodeDigits;
                else
                    olc = new OpenLocationCode(y, x, 11).CodeDigits.Substring(6, 5); //This takes lat, long, Coordinate takes X, Y. This line is correct.
                return olc + "|" + area;
            }
            return "";
        }

        public static string DetermineAreaPoint(List<MapData> entriesHere)
        {
            //New sorting rules:
            //If there's only one place, take it without any additional queries. Otherwise:
            //if there's a Point in the mapdata list, take the first one (No additional sub-sorting applied yet)
            //else if there's a Line in the mapdata list, take the first one (no additional sub-sorting applied yet)
            //else if there's polygonal areas here, take the smallest one by area 
            //(In general, the smaller areas should be overlaid on larger areas. This is more accurate than guessing by area types which one should be applied)

            if (entriesHere.Count() == 1)
                return entriesHere.First().name + "|" + entriesHere.First().AreaTypeId;

            var point = entriesHere.Where(e => e.place.GeometryType == "Point").FirstOrDefault();
            if (point != null)
                return point.name + "|" + point.AreaTypeId;

            var line = entriesHere.Where(e => e.place.GeometryType == "LineString" || e.place.GeometryType == "MultiLineString").FirstOrDefault();
            if (line != null)
                return line.name + "|" + line.AreaTypeId;

            var smallest = entriesHere.Where(e => e.place.GeometryType == "Polygon" || e.place.GeometryType == "MultiPolygon").OrderBy(e => e.place.Area).First();
            return smallest.name + "|" + smallest.AreaTypeId;
        }


        //Update this function if the logic in the above function changes too.
        public static int DetermineAreaType(List<MapData> entriesHere)
        {
            //New sorting rules:
            //If there's only one place, take it without any additional queries. Otherwise:
            //if there's a Point in the mapdata list, take the first one (No additional sub-sorting applied yet)
            //else if there's a Line in the mapdata list, take the first one (no additional sub-sorting applied yet)
            //else if there's polygonal areas here, take the smallest one by area 
            //(In general, the smaller areas should be overlaid on larger areas. This is more accurate than guessing by area types which one should be applied)

            if (entriesHere.Count() == 1)
                return entriesHere.First().AreaTypeId;

            var point = entriesHere.Where(e => e.place.GeometryType == "Point").FirstOrDefault();
            if (point != null)
                return point.AreaTypeId;

            var line = entriesHere.Where(e => e.place.GeometryType == "LineString" || e.place.GeometryType == "MultiLineString").FirstOrDefault();
            if (line != null)
                return line.AreaTypeId;

            var smallest = entriesHere.Where(e => e.place.GeometryType == "Polygon" || e.place.GeometryType == "MultiPolygon").OrderBy(e => e.place.Area).First();
            return smallest.AreaTypeId;
        }

        public static string LoadDataOnArea(long id)
        {
            //Debugging helper call. Loads up some information on an area and display it.
            var db = new GpsExploreContext();
            var entries = db.MapData.Where(m => m.WayId == id || m.RelationId == id || m.NodeId == id).ToList();
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

        public static MapData ConvertWayToMapData(ref WayData w)
        {
            //An entry with no name and no type is probably a relation support entry.
            //Something with a type and no name was found to be interesting but not named
            //something with a name and no type is probably an excluded entry.
            //I always want a type. Names are optional.
            if (w.AreaType == "") //w.name == "" && 
                return null;

            //Take a single tagged Way, and make it a usable MapData entry for the app.
            MapData md = new MapData();
            md.name = w.name;
            md.WayId = w.id;
            md.type = w.AreaType;
            md.AreaTypeId = MapSupport.areaTypeReference[w.AreaType.StartsWith("admin") ? "admin" : w.AreaType].First();

            //Adding support for LineStrings. A lot of rivers/streams/footpaths are treated this way.
            if (w.nds.First().id != w.nds.Last().id)
            {
                LineString temp2 = factory.CreateLineString(w.nds.Select(n => new Coordinate(n.lon, n.lat)).ToArray());
                md.place = temp2;
            }
            else
            {
                if (w.nds.Count <= 3)
                {
                    Log.WriteLog("Way " + w.id + " doesn't have enough nodes to parse into a Polygon. This entry is an awkward line, not processing.");
                    return null;
                }

                Polygon temp = factory.CreatePolygon(MapSupport.WayToCoordArray(w));
                md.place = MapSupport.SimplifyArea(temp);
                if (md.place == null)
                {
                    Log.WriteLog("Way " + w.id + " needs more work to be parsable, it's not counter-clockwise forward or reversed.");
                    return null;
                }
                if (!md.place.IsValid)
                {
                    Log.WriteLog("Way " + w.id + " needs more work to be parsable, it's not valid according to its own internal check.");
                    return null;
                }

                md.WayId = w.id;
            }
            w = null;
            return md;

        }

        public static MapData ConvertNodeToMapData(NodeReference n)
        {
            return new MapData()
            {
                name = n.name,
                type = n.type,
                place = factory.CreatePoint(new Coordinate(n.lon, n.lat)),
                NodeId = n.Id,
                AreaTypeId = MapSupport.areaTypeReference[n.type.StartsWith("admin") ? "admin" : n.type].First()
        };
        }

        public static string GetElementName(TagsCollectionBase tagsO)
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

        public static int GetTypeId(TagsCollectionBase tags)
        {
            //gets the ID associated with a type.
            string results = GetType(tags);
            if (results.StartsWith("admin")) //all admin levels are treated as the same type
                return areaTypeReference["admin"].First();

            return areaTypeReference[results].First();
        }

        public static string GetType(TagsCollectionBase tagsO)
        {
            //This is how we will figure out which area a cell counts as now.
            //Should make sure all of these exist in the AreaTypes table I made.
            //REMEMBER: this list needs to match the same tags as the ones in GetWays(relations)FromPBF or else data looks weird.
            //TODO: optimize this function, searching  the array 20 times in the worst-case scenario isn't good for big files. Takes an hour for a 7GB file.

            if (tagsO.Count() == 0)
                return ""; //Sanity check

            var tags = tagsO.ToLookup(k => k.Key, v => v.Value);
            //Entries are currently sorted by rough frequency of occurrence in my home area.

            //Water spaces should be displayed. Not sure if I want players to be in them for resources.
            //Water should probably override other values as a safety concern?
            if (DbSettings.processWater && tags["natural"].Any(v => v == "water") || tags["waterway"].Count() > 0)
                return "water";

            //Trail. Will likely show up in varying places for various reasons. Trying to limit this down to hiking trails like in parks and similar.
            //In my local park, i see both path and footway used (Footway is an unpaved trail, Path is a paved one)
            //highway=track is for tractors and other vehicles.  Don't include. that.
            //highway=path is non-motor vehicle, and otherwise very generic. 5% of all Ways in OSM.
            //highway=footway is pedestrian traffic only, maybe including bikes. Mostly sidewalks, which I dont' want to include.
            //highway=bridleway is horse paths, maybe including pedestrians and bikes
            //highway=cycleway is for bikes, maybe including pedesterians.
            if (DbSettings.processTrail && tags["highway"].Any(v => relevantTrailValues.Contains(v)
                && !tags["footway"].Any(v => v == "sidewalk" || v == "crossing")))
                return "trail";

            //Parks are good. Possibly core to this game.
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

            //Generic shopping area is ok. I don't want to advertise businesses, but this is a common area type.
            //TODO: expand this to buildings, not just landuse?
            //Landuse=retail has 200k entries. building=retail has 500k entries.
            //shop=* has about 5 million entries, mostly nodes.
            //Malls should be merged here, and are a sub-set of shop entries
            if (DbSettings.processRetail && (tags["landuse"].Any(v => v == "retail")
                || tags["building"].Any(v => v == "retail")
                || tags["shop"].Count() > 0)) // mall is a value of shop, so those are included here now.
                return "retail";

            //I have historical as a tag to save, but not necessarily sub-sets yet of whats interesting there.
            //NOTE: the OSM tag doesn't match my value
            if (DbSettings.processHistorical && tags["historic"].Count() > 0)
                return "historical";

            //Wetlands should also be high priority.
            if (DbSettings.processWetland && tags["natural"].Any(v => v == "wetland"))
                return "wetland";

            //Nature Reserve. Should be included
            if (DbSettings.processNatureReserve && tags["leisure"].Any(v => v == "nature_reserve"))
                return "natureReserve";

            //I have tourism as a tag to save, but not necessarily sub-sets yet of whats interesting there.
            if (DbSettings.processTourism && tags["tourism"].Any(v => relevantTourismValues.Contains(v)))
                return "tourism"; //TODO: create sub-values for tourism types?

            //Universities are good. Primary schools are not so good.  Don't include all education values.
            if (DbSettings.processUniversity && tags["amenity"].Any(v => v == "university" || v == "college"))
                return "university";

            //Beaches are good. Managed beaches are less good but I'll count those too.
            if (DbSettings.processBeach && tags["natural"].Any(v => v == "beach")
            || (tags["leisure"].Any(v => v == "beach_resort")))
                return "beach";

            //These additional types below this are intended for a more detailed, single-area focused game and not
            //the general explore game context.

            //Roads will matter
            if (DbSettings.processRoads && tags["highway"].Any(v => relevantRoadValues.Contains(v))
            && !tags["footway"].Any(v => v == "sidewalk" || v == "crossing"))
                return "road";

            //Amenities are a separate tag, so i want to pull them out separately from (and before) buildings
            //There are lots of amenities, though, and i need to figure out the list that applies here 
            //without interfering with other types of areas. 

            //buildings will matter
            //Mark abandoned/unused buildings?
            if (DbSettings.processBuildings && tags.Contains("building"))
                return "building";

            //Parking lots should get drawn too.
            if (DbSettings.processParking && tags["amenity"].Any(v => v == "parking"))
                return "parking";

            


            //Possibly of interest:
            //landuse:forest / landuse:orchard  / natural:wood
            //natural:sand may be of interest for desert area?
            //natural:spring / natural:hot_spring
            //amenity:theatre for plays/music/etc (amenity:cinema is a movie theater)
            //Anything else seems un-interesting or irrelevant.

            return ""; //not a type we need to save right now.
        }

        public static Coordinate[] WayToCoordArray(Support.WayData w)
        {
            if (w == null)
                return null;

            List<Coordinate> results = new List<Coordinate>();
            results.Capacity = w.nds.Count();

            foreach (var node in w.nds)
                results.Add(new Coordinate(node.lon, node.lat));

            return results.ToArray();
        }

        public static string GetPlusCode(double lat, double lon)
        {
            return OpenLocationCode.Encode(lat, lon).Replace("+", "");
        }

        public static CoordPair GetRandomPoint()
        {

            //Global scale testing.
            Random r = new Random();
            float lat = 90 * (float)r.NextDouble() * (r.Next() % 2 == 0 ? 1 : -1);
            float lon = 180 * (float)r.NextDouble() * (r.Next() % 2 == 0 ? 1 : -1);
            return new CoordPair(lat, lon);
        }

        public static CoordPair GetRandomBoundedPoint()
        {
            //randomize lat and long to roughly somewhere in Ohio. For testing a limited geographic area.
            //42, -80 NE
            //38, -84 SW
            //so 38 + (0-4), -84 = (0-4) coords.
            Random r = new Random();
            float lat = 38 + ((float)r.NextDouble() * 4);
            float lon = -84 + ((float)r.NextDouble() * 4);
            return new CoordPair(lat, lon);
        }

        public static void InsertAreaTypes()
        {
            var db = new GpsExploreContext();
            db.Database.BeginTransaction();
            db.Database.ExecuteSqlRaw("SET IDENTITY_INSERT AreaTypes ON;");
            db.AreaTypes.AddRange(areaTypes);
            db.SaveChanges();
            db.Database.ExecuteSqlRaw("SET IDENTITY_INSERT dbo.AreaTypes OFF;");
            db.Database.CommitTransaction();
        }

        public static Geometry SimplifyArea(Geometry place)
        {
            //Note: SimplifyArea CAN reverse a polygon's orientation, especially in a multi-polygon, so don't do CheckCCW until after
            var simplerPlace = NetTopologySuite.Simplify.TopologyPreservingSimplifier.Simplify(place, resolution10); //This cuts storage space for files by 30-50%  (40MB Ohio-water vs 26MB simplified)
            if (simplerPlace is Polygon)
            {
                simplerPlace = CCWCheck((Polygon)simplerPlace);
                if (simplerPlace == null)
                    return null; //isn't correct in either orientation.
                return simplerPlace;
            }
            else if (simplerPlace is MultiPolygon)
            {
                MultiPolygon mp = (MultiPolygon)simplerPlace;
                for(int i = 0; i < mp.Geometries.Count(); i++)
                {
                    mp.Geometries[i] = CCWCheck((Polygon)mp.Geometries[i]);
                }
                if (mp.Geometries.Count(g => g == null) == 0)
                    return mp;
                return null; //some of the outer shells aren't compatible. Should alert this to the user if possible.
            }
            return simplerPlace;
        }

        public static GeoPoint ProxyLocation(double lat, double lon, GeoArea bounds)
        {
            //Treat the user like they're in the real-world location
            //Mod their location by the box size, then add that to the minimum to get their new location
            var shiftX = lon % bounds.LongitudeWidth;
            var shiftY = lat % bounds.LatitudeHeight;

            double newLat = bounds.SouthLatitude + shiftY;
            double newLon = bounds.WestLongitude + shiftX;

            return new GeoPoint(newLat, newLon);
        }

        public static Polygon CCWCheck(Polygon p)
        {
            if (p == null)
                return null;

            if (p.NumPoints < 4)
                //can't determine orientation, because this point was shortened to an awkward line.
                return null;

            //Sql Server Geography type requires polygon points to be in counter-clockwise order.  This function returns the polygon in the right orientation, or null if it can't.
            if (p.Shell.IsCCW)
                return p;
            p = (Polygon)p.Reverse();
            if (p.Shell.IsCCW)
                return p;

            return null; //not CCW either way? Happen occasionally for some reason, and it will fail to write to the DB
        }

        public static void DownloadPbfFile(string topLevel, string subLevel1, string subLevel2)
        {
            //pull a fresh copy of a file from geofabrik.de (or other mirror potentially)
            //save it to the same folder as configured for pbf files (might be passed in)
            //web paths http://download.geofabrik.de/north-america/us/ohio-latest.osm.pbf
            //root, then each parent division. Starting with USA isn't too hard.
            //topLevel = "north-america";
            //subLevel1 = "us";
            //subLevel2 = "ohio";
            var wc = new WebClient();
            wc.DownloadFile("http://download.geofabrik.de/" + topLevel + "/" + subLevel1 + "/" + subLevel2 + "-latest.osm.pbf", subLevel2 + "-latest.osm.pbf");
        }

        public static byte[] GetAreaMapTile(ref List<MapData> allPlaces, GeoArea totalArea)
        {
            List<MapData> rowPlaces;
            //create a new bitmap.
            MemoryStream ms = new MemoryStream();
            //pixel formats. RBGA32 allows for hex codes. RGB24 doesnt?
            int imagesize = (int)Math.Floor(totalArea.LatitudeHeight / resolution10); //scales to area size
            using (var image = new Image<Rgba32>(imagesize, imagesize)) //each 10 cell in this area is a pixel.
            {
                image.Mutate(x => x.Fill(Rgba32.ParseHex(MapSupport.areaColorReference[0].First()))); //set all the areas to the background color
                for (int y = 0; y < image.Height; y++)
                {
                    //Dramatic performance improvement by limiting this to just the row's area. from 100+ seconds to 4.
                    rowPlaces = MapSupport.GetPlaces(new GeoArea(new GeoPoint(totalArea.Min.Latitude + (MapSupport.resolution10 * y), totalArea.Min.Longitude), new GeoPoint(totalArea.Min.Latitude + (MapSupport.resolution10 * (y + 1)), totalArea.Max.Longitude)), allPlaces);

                    Span<Rgba32> pixelRow = image.GetPixelRowSpan(image.Height - y - 1); //Plus code data is searched south-to-north, image is inverted otherwise.
                    for (int x = 0; x < image.Width; x++)
                    {
                        //Set the pixel's color by its type.
                        int placeData = MapSupport.GetAreaTypeFor10Cell(totalArea.Min.Longitude + (MapSupport.resolution10 * x), totalArea.Min.Latitude + (MapSupport.resolution10 * y), ref rowPlaces);
                        if (placeData != 0)
                        {
                            var color = MapSupport.areaColorReference[placeData].First();
                            pixelRow[x] = Rgba32.ParseHex(color); //set to appropriate type color
                        }
                    }
                }

                image.SaveAsPng(ms); //~25-40ms
            } //image disposed here.

           return ms.ToArray();
        }
        
        // as above but each pixel is an 11 cell instead of a 10 cell. more detail but slower.
        public static byte[] GetAreaMapTile11(ref List<MapData> allPlaces, GeoArea totalArea)
        {
            List<MapData> rowPlaces;
            //create a new bitmap.
            MemoryStream ms = new MemoryStream();
            //pixel formats. RBGA32 allows for hex codes. RGB24 doesnt?
            int imagesizeX = (int)Math.Floor(totalArea.LongitudeWidth / resolution11Lon); //scales to area size
            int imagesizeY = (int)Math.Floor(totalArea.LatitudeHeight / resolution11Lat); //scales to area size
            using (var image = new Image<Rgba32>(imagesizeX, imagesizeY)) //each 11 cell in this area is a pixel.
            {
                image.Mutate(x => x.Fill(Rgba32.ParseHex(MapSupport.areaColorReference[0].First()))); //set all the areas to the background color
                for (int y = 0; y < image.Height; y++)
                {
                    //Dramatic performance improvement by limiting this to just the row's area. from 100+ seconds to 4.
                    rowPlaces = MapSupport.GetPlaces(new GeoArea(new GeoPoint(totalArea.Min.Latitude + (MapSupport.resolution11Lat * y), totalArea.Min.Longitude), new GeoPoint(totalArea.Min.Latitude + (MapSupport.resolution11Lat * (y + 1)), totalArea.Max.Longitude)), allPlaces);

                    Span<Rgba32> pixelRow = image.GetPixelRowSpan(image.Height - y - 1); //Plus code data is searched south-to-north, image is inverted otherwise.
                    for (int x = 0; x < image.Width; x++)
                    {
                        //Set the pixel's color by its type.
                        int placeData = MapSupport.GetAreaTypeFor11Cell(totalArea.Min.Longitude + (MapSupport.resolution11Lon * x), totalArea.Min.Latitude + (MapSupport.resolution11Lat * y), ref rowPlaces);
                        if (placeData != 0)
                        {
                            var color = MapSupport.areaColorReference[placeData].First();
                            pixelRow[x] = Rgba32.ParseHex(color); //set to appropriate type color
                        }
                    }
                }

                image.SaveAsPng(ms);
            } //image disposed here.

            return ms.ToArray();
        }
    }
}
