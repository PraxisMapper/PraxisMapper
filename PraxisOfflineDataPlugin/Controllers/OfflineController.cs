using Google.OpenLocationCode;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using PraxisCore;
using PraxisCore.Support;
using PraxisMapper.Classes;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Xml.Schema;
using static Azure.Core.HttpHeader;
using static PraxisCore.DbTables;

namespace PraxisOfflineDataPlugin.Controllers
{
    [Route("[controller]")]
    public class OfflineController : Controller, IPraxisPlugin
    {
        //NOTE: for more accurate data, I could save cell10 info in the final dictionary.
        //THis would be be better saved for a Cell6 or smaller area, but that could be generated on demand once,
        //then saved and sent on every other request.
        [HttpGet]
        [Route("/[controller]/Small/{plusCode6}")]
        public string GetSmallTerrainData(string plusCode6)
        {
            Response.Headers.Add("X-noPerfTrack", "Offline/Small/VARSREMOVED");
            GeoArea box6 = plusCode6.ToGeoArea();
            var quickplaces = PraxisCore.Place.GetPlaces(box6);
            if (quickplaces.Count == 0)
                return "";

            foreach (var place in quickplaces)
                if (place.ElementGeometry.Coordinates.Length > 1000)
                    place.ElementGeometry = place.ElementGeometry.Intersection(box6.ToPolygon());

            string cell2 = plusCode6.Substring(0, 2);
            string cell4 = plusCode6.Substring(2, 2);
            string cell6 = plusCode6.Substring(4, 2);

            //This saves the smallest amount of information on area types possible.
            //It checks each map tile (Cell8) in the requested Cell6, logs the presence of which terrain types are in it, 
            //and saves it as that type's index, to be the smallest possible JSON value for a client to use.
            var terrainDict = new ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentDictionary<string, string>>>>();
            terrainDict[cell2] = new ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentDictionary<string, string>>>();
            terrainDict[cell2][cell4] = new ConcurrentDictionary<string, ConcurrentDictionary<string, string>>();
            terrainDict[cell2][cell4][cell6] = new ConcurrentDictionary<string, string>();

            var index = GetTerrainIndex();
            //To avoid creating a new type, I add the index data as its own entry, and put all the data in the first key under "index".
            terrainDict["index"] = new ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentDictionary<string, string>>>();
            terrainDict["index"][String.Join("|", index.Select(i => i.Key + "," + i.Value))] = new ConcurrentDictionary<string, ConcurrentDictionary<string, string>>();

            Parallel.ForEach(GetCellCombos(), (cell8) =>
            {
                string pluscode = plusCode6 + cell8;
                GeoArea box = pluscode.ToGeoArea();
                var places = PraxisCore.Place.GetPlaces(box, quickplaces);
                if (places.Count() == 0)
                    return;

                places = places.Where(p => p.IsGameElement).ToList();
                if (places.Count() == 0)
                    return;
                var terrainInfo = AreaStyle.GetAreaDetails(ref box, ref places);
                var terrainsPresent = terrainInfo.Select(t => t.data.style).Distinct().ToList();

                if (terrainsPresent.Count > 0)
                {
                    string concatTerrain = String.Join("|", terrainsPresent.Select(t => index[t]));
                    terrainDict[cell2][cell4][cell6][cell8] = concatTerrain;
                }
            });

            return JsonSerializer.Serialize(terrainDict);
        }

        [HttpGet]
        [Route("/[controller]/All")]
        public string GetAllOfflineData()
        {
            //NOTE: this is not intended to be called by users, since this reads the entire database. 
            //This is for an admin to use to create data for an application to reference.
            PraxisAuthentication.GetAuthInfo(Response, out var accountId, out var password);
            if (!PraxisAuthentication.IsAdmin(accountId))
                return "";

            using var db = new PraxisContext();
            db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var terrainDict = new ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentDictionary<string, string>>>>();
            var index = GetTerrainIndex();
            //To avoid creating a new type, I add the index data as its own entry, and put all the data in the first key under "index".
            terrainDict["index"] = new ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentDictionary<string, string>>>();
            terrainDict["index"][String.Join("|", index.Select(i => i.Key + "," + i.Value))] = new ConcurrentDictionary<string, ConcurrentDictionary<string, string>>();

            foreach (var cell2 in GetCell2Combos())
            {
                var place2 = cell2.ToPolygon();
                var placeTest = db.Places.Any(p => p.ElementGeometry.Intersects(place2));
                if (!placeTest)
                    continue;

                terrainDict[cell2] = new ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentDictionary<string, string>>>();
                foreach (var cell4 in GetCellCombos())
                {
                    terrainDict[cell2][cell4] = new ConcurrentDictionary<string, ConcurrentDictionary<string, string>>();
                    foreach (var cell6 in GetCellCombos())
                    {
                        string pluscode6 = cell2 + cell4 + cell6;
                        GeoArea box6 = pluscode6.ToGeoArea();
                        var quickplaces = PraxisCore.Place.GetPlaces(box6);
                        if (quickplaces.Count == 0)
                            continue;

                        terrainDict[cell2][cell4][cell6] = new ConcurrentDictionary<string, string>();

                        foreach (var place in quickplaces)
                            if (place.ElementGeometry.Coordinates.Length > 1000)
                                place.ElementGeometry = place.ElementGeometry.Intersection(box6.ToPolygon());

                        Parallel.ForEach(GetCellCombos(), (cell8) =>
                        {
                            string pluscode = pluscode6 + cell8;
                            GeoArea box = pluscode.ToGeoArea();
                            var places = PraxisCore.Place.GetPlaces(box, quickplaces);
                            if (places.Count == 0)
                                return;

                            places = places.Where(p => p.IsGameElement).ToList();
                            if (places.Count == 0)
                                return;
                            var terrainInfo = AreaStyle.GetAreaDetails(ref box, ref places);
                            var terrainsPresent = terrainInfo.Select(t => t.data.style).Distinct().ToList();

                            if (terrainsPresent.Count > 0)
                            {
                                string concatTerrain = String.Join("|", terrainsPresent.Select(t => index[t])); //indexed ID of each type.
                                terrainDict[cell2][cell4][cell6][cell8] = concatTerrain;
                            }
                        });
                        if (terrainDict[cell2][cell4][cell6].IsEmpty)
                            terrainDict[cell2][cell4].TryRemove(cell6, out _);
                    }
                    if (terrainDict[cell2][cell4].IsEmpty)
                        terrainDict[cell2].TryRemove(cell4, out _);
                }
                if (terrainDict[cell2].IsEmpty)
                    terrainDict[cell2].TryRemove(cell2, out _);

                //NOTE and TODO: if I want to save files per Cell2, here is where I should write terrainDict, then remove the current Cell2 entry and loop.
            }

            return JsonSerializer.Serialize(terrainDict);
        }

        private static Dictionary<string, int> GetTerrainIndex(string style = "mapTiles")
        {
            var dict = new Dictionary<string, int>();
            foreach (var entry in TagParser.allStyleGroups[style])
            {
                if (entry.Value.IsGameElement)
                {
                    dict.Add(entry.Key, dict.Count + 1);
                }
            }
            return dict;
        }

        static List<string> GetCellCombos()
        {
            var list = new List<string>(400);
            foreach (var Yletter in OpenLocationCode.CodeAlphabet)
                foreach (var Xletter in OpenLocationCode.CodeAlphabet)
                {
                    list.Add(String.Concat(Yletter, Xletter));
                }

            return list;
        }

        static List<string> GetCell2Combos()
        {
            var list = new List<string>(400);
            foreach (var Yletter in OpenLocationCode.CodeAlphabet.Take(9))
                foreach (var Xletter in OpenLocationCode.CodeAlphabet.Take(18))
                {
                    list.Add(String.Concat(Yletter, Xletter));
                }

            return list;
        }

        //TODO: test these formats and clean them up to take less space. short-names and all that.
        public class OfflineDataV2
        {
            public List<OfflinePlaceEntry> entries { get; set; }
            public Dictionary<int, string> nameTable { get; set; } //id, name
            public Dictionary<string, int> gridNames { get; set; } //pluscode, nameTable entry id
        }

        public class OfflinePlaceEntry
        {
            public int nid { get; set; } //nametable id
            public int tid { get; set; } //terrain id, which style entry this place is
            public int gt { get; set; } //geometry type. 1 = point, 2 = line OR hollow shape, 3 = filled shape.
            //public string geometry { get; set; }
            public string p { get; set; } //Points, local to the given PlusCode
        }

        [HttpGet]
        [Route("/[controller]/V2/{plusCode}")]
        [Route("/[controller]/V2/{plusCode}/{styleSet}")]
        public string GetOfflineDataV2(string plusCode, string styleSet = "mapTiles")
        {
            //Trying a second approach to this. I want a smaller set of data, but I also want to expand whats available in this.
            //This assumes that a server exists, but it lives primarily NOT to draw tiles itself, but to parse data down for a specific game client which does that work.
            //adding: nametable per Cell10, and possibly geometry. This may get limited to the cell8 itself so its self-contained and only whats used gets loaded.
            using var db = new PraxisContext();
            db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            db.ChangeTracker.AutoDetectChangesEnabled = false;

            //save and load this from the db if we've generated it before.
            var existingData = db.AreaData.FirstOrDefault(a => a.PlusCode == plusCode && a.DataKey == "offlineV2");
            if (existingData != null)
                return existingData.DataValue.ToUTF8String();

            var cell8 = plusCode.ToGeoArea();
            var cell8Poly = cell8.ToPolygon();
            var placeData = PraxisCore.Place.GetPlaces(cell8, styleSet: styleSet, dataKey: styleSet);
            int nameCounter = 1; //used to determine nametable key

            List<OfflinePlaceEntry> entries = new List<OfflinePlaceEntry>(placeData.Count);
            Dictionary<string, int> nametable = new Dictionary<string, int>(); //name, id

            var min = cell8.Min;
            foreach (var place in placeData)
            {
                place.ElementGeometry = place.ElementGeometry.Intersection(cell8Poly).Simplify(ConstantValues.resolutionCell11Lon);

                var name = TagParser.GetName(place);
                int nameID = 0;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    if (!nametable.TryGetValue(name, out var nameval))
                    {
                        nameval = nameCounter;
                        nametable.Add(name, nameval);
                        nameCounter++;
                        //attach to this item.
                    }
                    nameID = nameval;
                }

                var style = TagParser.allStyleGroups[styleSet][place.StyleName];

                //I'm locking these geometry items to a tile, So I convert these points in the geometry to integers, effectively
                //letting me draw Cell11 pixel-precise points from this info, and is shorter stringified for JSON vs floats/doubles.
                var coordSets = GetCoordEntries(place, cell8.Min);
                foreach (var coordSet in coordSets)
                {
                    var offline = new OfflinePlaceEntry();
                    offline.nid = nameID;
                    offline.tid = style.MatchOrder; //Client will need to know what this ID means.
                    
                    offline.gt = place.ElementGeometry.GeometryType == "Point" ? 1 : place.ElementGeometry.GeometryType == "LineString" ? 2 : style.PaintOperations.All(p => p.FillOrStroke == "stroke") ? 2 : 3;   
                    offline.p = coordSet;
                    entries.Add(offline);
                }
            }

            //TODO: this is not yet totally optimized, but it's sufficient fast as-is for use.
            var cell82 = (GeoArea)cell8;
            var terrainInfo = AreaStyle.GetAreaDetails(ref cell82, ref placeData);
            Dictionary<string, int> nameInfo = terrainInfo.Where(t => t.data.name != "").Select(t => new { t.plusCode, nameId = nametable[t.data.name] }).ToDictionary(k => k.plusCode.Replace(plusCode, ""), v => v.nameId);

            var finalData = new OfflineDataV2();
            finalData.nameTable = nametable.ToDictionary(k => k.Value, v => v.Key);
            finalData.entries = entries;
            finalData.gridNames = nameInfo;

            string data = JsonSerializer.Serialize(finalData);
            GenericData.SetAreaData(plusCode, "offlineV2", data, (10 * 365 * 24 * 60 * 1000.0)); //10 years in ms?
            return data;
        }

        public List<string> GetCoordEntries(DbTables.Place place, GeoPoint min)
        {
            List<string> points = new List<string>();

            if (place.ElementGeometry.GeometryType == "MultiPolygon")
            {
                foreach (var poly in ((MultiPolygon)place.ElementGeometry).Geometries) //This should be the same as the Polygon code below.
                {
                    points.AddRange(GetPolygonPoints(poly as Polygon, min));
                }
            }
            else if (place.ElementGeometry.GeometryType == "Polygon")
            {
                points.AddRange(GetPolygonPoints(place.ElementGeometry as Polygon, min));
            }
            else
                points.Add(string.Join("|", place.ElementGeometry.Coordinates.Select(c => (int)((c.X - min.Longitude) / ConstantValues.resolutionCell11Lon) + "," + ((int)((c.Y - min.Latitude) / ConstantValues.resolutionCell11Lat)))));           

            return points;
        }

        public static List<string> GetPolygonPoints(Polygon p, GeoPoint min)
        {
            List<string> results = new List<string>();
            if (p.Holes.Length == 0)
                results.Add(string.Join("|", p.Coordinates.Select(c => (int)((c.X - min.Longitude) / ConstantValues.resolutionCell11Lon) + "," + ((int)((c.Y - min.Latitude) / ConstantValues.resolutionCell11Lat)))));
            else
            {
                //Split this polygon into smaller pieces, split on the center of each hole present longitudinally
                //West to east direction chosen arbitrarily.
                var westEdge = p.Coordinates.Min(c => c.X);
                var northEdge = p.Coordinates.Max(c => c.Y);
                var southEdge = p.Coordinates.Min(c => c.Y);

                List<double> splitPoints = new List<double>();
                foreach (var hole in p.Holes.OrderBy(h => h.Centroid.X))
                    splitPoints.Add(hole.Centroid.X);

                foreach (var point in splitPoints)
                {
                    var splitPoly = new GeoArea(southEdge, westEdge, northEdge, point).ToPolygon();
                    var subPoly = p.Intersection(splitPoly);

                    //Still need to check that we have reasonable geometry here.
                    if (subPoly.GeometryType == "Polygon")
                        results.AddRange(GetPolygonPoints(subPoly as Polygon, min));
                    else if (subPoly.GeometryType == "MultiPolygon")
                    {
                        foreach (var p2 in ((MultiPolygon)subPoly).Geometries)
                            results.AddRange(GetPolygonPoints(p2 as Polygon, min));
                    }
                    else
                        ErrorLogger.LogError(new Exception("Offline proccess error: Got geoType " + subPoly.GeometryType + ", which wasnt expected"));
                    westEdge = point;
                }
            }
            return results.Distinct().ToList(); //In the unlikely case splitting ends up processing the same part twice
        }

        public class OfflineDrawOps
        {
            public string color { get; set; }
            public double sizePx { get; set; }
        }

        public class OfflineStyleItem
        {
            public List<OfflineDrawOps> drawOps { get; set; }

        }

        [HttpGet]
        [Route("/[controller]/Style/{styleSet}")]
        public string GetStyleForClient(string styleSet)
        {
            Dictionary<string, OfflineStyleItem> styles = new Dictionary<string, OfflineStyleItem>();

            //OR is this a Dict<string, OfflineSTyleITem>?
            var style = TagParser.allStyleGroups[styleSet];
            foreach(var styleEntry in style)
            {
                var entry = new OfflineStyleItem()
                {
                    drawOps = styleEntry.Value.PaintOperations.OrderByDescending(p => p.LayerId).Select(p => new OfflineDrawOps()
                    { color = p.HtmlColorCode, sizePx = Math.Round(p.LineWidthDegrees / ConstantValues.resolutionCell11Lat, 1)}).ToList()
                };
                styles.Add(styleEntry.Value.MatchOrder.ToString(), entry);
            }

            return JsonSerializer.Serialize(styles);
        }
    }
}