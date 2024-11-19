using Google.OpenLocationCode;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using PraxisCore;
using PraxisCore.Support;
using PraxisMapper.Classes;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text.Json;

namespace PraxisOfflineDataPlugin.Controllers
{
    [Route("[controller]")]
    public class OfflineController : Controller, IPraxisPlugin
    {
        //NOTE: for more accurate data, I could save cell10 info in the final dictionary.
        //THis would be be better saved for a Cell6 or smaller area, but that could be generated on demand once,
        //then saved and sent on every other request.

        //TODO: the offline CONTROLLER will use the Places table because its a live server. The Larry Offline commands will use the OfflinePlaces table.
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
                if (places.Count == 0)
                    return;

                places = places.Where(p => p.IsGameElement).ToList();
                if (places.Count == 0)
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
            public string PlusCode { get; set; }
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
        [Route("/[controller]/V2/{plusCode}/{styles}")]
        public string GetOfflineDataV2(string plusCode, string styles = "mapTiles")
        {
            //OfflineData.MakeOfflineJson(plusCode); //TODO: this doesnt work for a single file, this is currently hard-set to use the zip file logic.

            var data = OfflineData.MakeEntries(plusCode, styles);
            var stringData = JsonSerializer.Serialize(data);
            GenericData.SetAreaData(plusCode, "offlineV2", stringData);
            return stringData;
        }

        static readonly JsonSerializerOptions jso = new JsonSerializerOptions() { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
        [HttpGet]
        [Route("/[controller]/V2Min/{plusCode}")]
        [Route("/[controller]/V2Min/{plusCode}/{styles}")]
        public string GetOfflineDataV2Min(string plusCode, string styles = "mapTiles")
        {
            //OfflineData.MakeOfflineJson(plusCode); //TODO: this doesnt work for a single file, this is currently hard-set to use the zip file logic.

            var data = OfflineData.MakeMinimizedOfflineEntries(plusCode, styles);
            var stringData = JsonSerializer.Serialize(data, jso);
            GenericData.SetAreaData(plusCode, "offlineV2Min", stringData);
            return stringData;
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

            if (points.Count == 0)
            {
                System.Diagnostics.Debugger.Break();
            }

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
            public int drawOrder { get; set; }
        }

        public class OfflineStyleItem
        {
            public string name { get; set; }
            public List<OfflineDrawOps> drawOps { get; set; }

        }

        [HttpGet]
        [Route("/[controller]/Style/{styleSet}")]
        public string GetStyleForClient(string styleSet)
        {
            Dictionary<string, OfflineStyleItem> styles = new Dictionary<string, OfflineStyleItem>();

            //OR is this a Dict<string, OfflineSTyleITem>?
            var style = TagParser.allStyleGroups[styleSet];
            foreach (var styleEntry in style)
            {
                var entry = new OfflineStyleItem()
                {
                    name = styleEntry.Value.Name,
                    drawOps = styleEntry.Value.PaintOperations.OrderByDescending(p => p.LayerId).Select(p => new OfflineDrawOps()
                    {
                        color = p.HtmlColorCode.Length == 8 ? p.HtmlColorCode.Substring(2) : p.HtmlColorCode,
                        sizePx = Math.Round(p.LineWidthDegrees / ConstantValues.resolutionCell12Lat, 1),
                        drawOrder = p.LayerId

                    }).ToList()
                };
                styles.Add(styleEntry.Value.MatchOrder.ToString(), entry);
            }

            return JsonSerializer.Serialize(styles);
        }

        [HttpGet]
        [Route("/[controller]/FromZip/{plusCode}")]
        public ActionResult GetJsonFromZippedFile(string plusCode)
        {
            //This is a helper file for pulling smaller files from the Cell4 zips.

            string cell4 = plusCode.Substring(0, 4);
            string cell6 = plusCode.Substring(0, 6);

            string zipPath = "./Content/OfflineData/" + cell4.Substring(0, 2) + "/" + cell4 + ".zip";

            string results = "";
            using (var fs = new FileStream(zipPath, FileMode.Open))
            {
                using (var zip = new ZipArchive(fs, ZipArchiveMode.Read))
                {
                    var entry = zip.GetEntry(cell6 + ".json");

                    var innerstream = entry.Open();
                    results = new StreamReader(innerstream).ReadToEnd();
                    innerstream.Close();
                    innerstream.Dispose();
                }
            }

            var byteResults = results.ToByteArrayUTF8();
            Response.ContentLength = byteResults.Length;
            return File(byteResults, "text/plain");
        }
    }
}