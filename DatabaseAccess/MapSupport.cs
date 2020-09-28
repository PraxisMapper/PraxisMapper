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

namespace DatabaseAccess
{
    public static class MapSupport
    {

        public const double resolution10 = .000125;
        public static List<string> relevantTags = new List<string>() { "name", "natural", "leisure", "landuse", "amenity", "tourism", "historic", "highway" }; //The keys in tags we process to see if we want it included.
        public static List<string> relevantTourismValues = new List<string>() { "artwork", "attraction", "gallery", "museum", "viewpoint", "zoo" }; //The stuff we care about in the tourism category. Zoo and attraction are debatable.
        public static List<string> relevantHighwayValues = new List<string>() { "path", "bridleway", "cycleway", "footway" }; //The stuff we care about in the tourism category. Zoo and attraction are debatable.
        public static List<MapData> GetPlaces(GeoArea area, List<MapData> source = null)
        {
            //The flexible core of the lookup functions. Takes an area, returns results that intersect from Source. If source is null, looks into the DB.
            //Intersects is the only indexable function on a geography column I would want here. Distance and Equals can also use the index, but I don't need those in this app.
            var db = new DatabaseAccess.GpsExploreContext();
            var factory = NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326); //SRID matches Plus code values.
            var coordSeq = MakeBox(area);
            var location = factory.CreatePolygon(coordSeq);
            List<MapData> places;
            if (source == null)
                places = db.MapData.Where(md => md.place.Intersects(location)).ToList(); //Doesn't seem to catch some very large ways, like Grand Canyon National Park. 2 ways for GCNP show up as SPOI entries. All
            else
                places = source.Where(md => md.place.Intersects(location)).ToList();
            return places;
        }

        public static Coordinate[] MakeBox(GeoArea plusCodeArea)
        {
            var factory = NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326); //SRID matches Plus code values.
            var cord1 = new Coordinate(plusCodeArea.Min.Longitude, plusCodeArea.Min.Latitude);
            var cord2 = new Coordinate(plusCodeArea.Min.Longitude, plusCodeArea.Max.Latitude);
            var cord3 = new Coordinate(plusCodeArea.Max.Longitude, plusCodeArea.Max.Latitude);
            var cord4 = new Coordinate(plusCodeArea.Max.Longitude, plusCodeArea.Min.Latitude);
            var cordSeq = new Coordinate[5] { cord4, cord3, cord2, cord1, cord4 };

            return cordSeq;
        }

        //TODO: move support classes from XmlParser to this DLL before this can work.
        //public static Coordinate[] MakeCoordinateArrayFromWay(Way w)
        //{
        //    var factory = NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326); //SRID matches Plus code values.

        //    return cordSeq;
        //}

        public static void SplitArea(GeoArea area, int divideCount, List<MapData> places, out List<MapData>[] placeArray, out GeoArea[] areaArray)
        {
            //Take area, divide it into a divideCount * divideCount grid of area. Return matching arrays of MapData and GeoArea, with indexes that correspond 1:1
            //The purpose of this function is to reduce code involved in optimizing a search, and to make it more flexible to test 
            //performance improvements on area splits.

            placeArray = new List<MapData>[1];
            areaArray = new GeoArea[1];

            //Logic note 1: if divideCount is 20, this is just reducing a PlusCode to the next smaller set of PlusCodes ranges.
            if (divideCount == 0 || divideCount == 1)
                return;

            var latDivider = area.LatitudeHeight / divideCount;
            var lonDivider = area.LongitudeWidth / divideCount;

            List<List<MapData>> resultsPlace = new List<List<MapData>>();
            List<GeoArea> resultsArea = new List<GeoArea>();

            for (var x = 0; x < divideCount; x++)
            {
                for (var y = 0; y < divideCount; y++)
                {
                    var box = new GeoArea(area.SouthLatitude + (latDivider * y), area.WestLongitude + (lonDivider * x), area.SouthLatitude + (latDivider * y) + latDivider, area.WestLongitude + (lonDivider * x) + lonDivider);
                    resultsPlace.Add(GetPlaces(box, places));
                    resultsArea.Add(box);
                }
            }

            placeArray = resultsPlace.ToArray();
            areaArray = resultsArea.ToArray();

            return;
        }

        public static StringBuilder SearchArea(GeoArea area, ref List<MapData> mapData)
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

                    var placesFound = MapSupport.FindPlacesIn10Cell(x, y, ref mapData);
                    if (!string.IsNullOrWhiteSpace(placesFound))
                        sb.AppendLine(placesFound);
                }
            }

            return sb;
        }

        public static string FindPlacesIn10Cell(double x, double y, ref List<MapData> places)
        {
            var box = new GeoArea(new GeoPoint(y, x), new GeoPoint(y + resolution10, x + resolution10));
            var entriesHere = MapSupport.GetPlaces(box, places);

            if (entriesHere.Count() == 0)
            {
                return "";
            }
            else
            {
                //Generally, if there's a smaller shape inside a bigger shape, the smaller one should take priority.
                var olc = new OpenLocationCode(y, x).CodeDigits.Substring(6, 4); //This takes lat, long, Coordinate takes X, Y. This line is correct.
                var smallest = entriesHere.Where(e => e.place.Area == entriesHere.Min(e => e.place.Area)).First();
                return olc + "|" + smallest.name + "|" + smallest.type;
            }
        }

        public static string LoadDataOnArea(long id)
        {
            var db = new GpsExploreContext();
            var entries = db.MapData.Where(m => m.WayId == id).ToList(); //was First,  was MapDataId
            string results = "";
            foreach (var entry in entries)
            {
                var shape = entry.place;
                
                results += "Name: " + entry.name + Environment.NewLine;
                results += "Game Type: " + entry.type + Environment.NewLine;
                results += "Geometry Type: " + shape.GeometryType + Environment.NewLine;
                results += "IsValid? : " + shape.IsValid + Environment.NewLine;
                results += "Area: " + shape.Area + Environment.NewLine;
                results += "As Text: " + shape.AsText() + Environment.NewLine;
            }
            
            return results;
        }

        //TODO: finish and test this function
        //TODO: make a Node version.
        public static MapData ConvertWayToMapData(DatabaseAccess.Support.Way w)
        {
            var factory = NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326);
            MapData md = new MapData();
            md.name = w.name;
            md.WayId = w.id;
            md.type = w.AreaType;

            //Adding support for single lines. A lot of rivers/streams/footpaths are treated this way.
            if (w.nds.First().id != w.nds.Last().id)
            {
                LineString temp2 = factory.CreateLineString(w.nds.Select(n => new Coordinate(n.lon, n.lat)).ToArray());
                md.place = temp2;
            }
            else
            {
                Polygon temp = factory.CreatePolygon(w.nds.Select(n => new Coordinate(n.lon, n.lat)).ToArray());
                if (!temp.Shell.IsCCW)
                {
                    temp = (Polygon)temp.Reverse();
                    if (!temp.Shell.IsCCW)
                    {
                        Log.WriteLog("Way " + w.id + " needs more work to be parsable, it's not counter-clockwise forward or reversed.");
                    }
                    if (!temp.IsValid)
                    {
                        Log.WriteLog("Way " + w.id + " needs more work to be parsable, it's not valid according to its own internal check.");
                    }
                }
                md.place = temp;
                md.WayId = w.id;
            }
            return md;
        }

        public static MapData ConvertNodeToMapData(OsmSharp.Node n)
        {
            var factory = NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326);
            return new MapData()
            {
                name = GetElementName(n.Tags),
                type = GetType(n.Tags),
                place = factory.CreatePoint(new Coordinate(n.Longitude.Value, n.Latitude.Value)),
                NodeId = n.Id
            };
        }

        public static string GetElementName(TagsCollectionBase tags)
        {
            string name = tags.Where(t => t.Key == "name").FirstOrDefault().Value;
            if (name == null)
                return "";
            return name; //.RemoveDiacritics(); //Not sure if my font needs this or not
        }

        public static string GetType(TagsCollectionBase tags)
        {
            //This is how we will figure out which area a cell counts as now.
            //Should make sure all of these exist in the AreaTypes table I made.
            //REMEMBER: this list needs to match the same tags as the ones in GetWays(relations)FromPBF or else data looks weird.
            //TODO: prioritize these tags, since each space gets one value.

            if (tags.Count() == 0)
                return ""; //Shouldn't happen, but as a sanity check if we start adding Nodes later.

            //Water spaces should be displayed. Not sure if I want players to be in them for resources.
            //Water should probably override other values as a safety concern.
            if (tags.Any(t => t.Key == "natural" && t.Value == "water")
                || tags.Any(t => t.Key == "waterway"))
                return "water";

            //Wetlands should also be high priority.
            if (tags.Any(t => (t.Key == "natural" && t.Value == "wetland")))
                return "wetland";

            //Parks are good. Possibly core to this game.
            if (tags.Any(t => t.Key == "leisure" && t.Value == "park"))
                return "park";

            //Beaches are good. Managed beaches are less good but I'll count those too.
            if (tags.Any(t => (t.Key == "natural" && t.Value == "beach")
            || (t.Key == "leisure" && t.Value == "beach_resort")))
                return "beach";

            //Universities are good. Primary schools are not so good.  Don't include all education values.
            if (tags.Any(t => (t.Key == "amenity" && t.Value == "university")
                || (t.Key == "amenity" && t.Value == "college")))
                return "university";

            //Nature Reserve. Should be included, but possibly secondary to other types inside it.
            if (tags.Any(t => (t.Key == "leisure" && t.Value == "nature_reserve")))
                return "natureReserve";

            //Cemetaries are ok. They don't seem to appreciate Pokemon Go, but they're a public space and often encourage activity in them (thats not PoGo)
            if (tags.Any(t => (t.Key == "landuse" && t.Value == "cemetery")
                || (t.Key == "amenity" && t.Value == "grave_yard")))
                return "cemetery";

            //Malls are a good indoor area to explore.
            if (tags.Any(t => t.Key == "shop" && t.Value == "mall"))
                return "mall";

            //Generic shopping area is ok. I don't want to advertise businesses, but this is a common area type.
            if (tags.Any(t => (t.Key == "landuse" && t.Value == "retail")))
                return "retail";

            //I have tourism as a tag to save, but not necessarily sub-sets yet of whats interesting there.
            if (tags.Any(t => (t.Key == "tourism" && relevantTourismValues.Contains(t.Value))))
                return "tourism"; //TODO: create sub-values for tourism types?

            //I have historical as a tag to save, but not necessarily sub-sets yet of whats interesting there.
            //NOTE: the OSM tag doesn't match my value
            if (tags.Any(t => (t.Key == "historic")))
                return "historical";

            //Trail. Will likely show up in varying places for various reasons. Trying to limit this down to hiking trails like in parks and similar.
            //In my local park, i see both path and footway used (Footway is an unpaved trail, Path is a paved one)
            //highway=track is for tractors and other vehicles.  Don't include. that.
            //highway=path is non-motor vehicle, and otherwise very generic. 5% of all Ways in OSM.
            //highway=footway is pedestrian traffic only, maybe including bikes. Mostly sidewalks, which I dont' want to include.
            //highway=bridleway is horse paths, maybe including pedestrians and bikes
            //highway=cycleway is for bikes, maybe including pedesterians.
            if (tags.Any(t => (t.Key == "highway" && t.Value == "bridleway"))
                || (tags.Any(t => t.Key == "highway" && t.Value == "path")) //path implies motor_vehicle = no // && !tags.Any(t => t.k =="motor_vehicle" && t.v == "yes")) //may want to check anyways?
                || (tags.Any(t => t.Key == "highway" && t.Value == "cycleway"))  //I probably want to include these too, though I have none local to see.
                || (tags.Any(t => t.Key == "highway" && relevantHighwayValues.Contains(t.Value)) && !tags.Any(t => t.Key == "footway" && (t.Value == "sidewalk" || t.Value == "crossing")))
                )
                return "trail";

            //Possibly of interest:
            //landuse:forest / landuse:orchard  / natural:wood
            //natural:sand may be of interest for desert area?
            //natural:spring / natural:hot_spring
            //amenity:theatre for plays/music/etc (amenity:cinema is a movie theater)
            //Anything else seems un-interesting or irrelevant.

            return ""; //not a way we need to save right now.
        }
    }
}
