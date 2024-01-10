using Google.OpenLocationCode;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using PraxisCore;
using PraxisCore.Support;
using PraxisMapper.Classes;
using System.Collections.Concurrent;
using System.Text.Json;

namespace PraxisOfflineDataPlugin.Controllers {
    [Route("[controller]")]
    public class OfflineController : Controller, IPraxisPlugin {
        //NOTE: for more accurate data, I could save cell10 info in the final dictionary.
        //THis would be be better saved for a Cell6 or smaller area, but that could be generated on demand once,
        //then saved and sent on every other request.
        [HttpGet]
        [Route("/[controller]/Small/{plusCode6}")]
        public string GetSmallTerrainData(string plusCode6) {
            Response.Headers.Add("X-noPerfTrack", "Offline/Small/VARSREMOVED");
            GeoArea box6 = plusCode6.ToGeoArea();
            var quickplaces = Place.GetPlaces(box6);
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

            Parallel.ForEach(GetCellCombos(), (cell8) => {
                string pluscode = plusCode6 + cell8;
                GeoArea box = pluscode.ToGeoArea();
                var places = Place.GetPlaces(box, quickplaces);
                if (places.Count == 0)
                    return;

                places = places.Where(p => p.IsGameElement).ToList();
                if (places.Count == 0)
                    return;
                var terrainInfo = AreaStyle.GetAreaDetails(ref box, ref places);
                var terrainsPresent = terrainInfo.Select(t => t.data.style).Distinct().ToList();

                if (terrainsPresent.Count > 0) {
                    string concatTerrain = String.Join("|", terrainsPresent.Select(t => index[t]));
                    terrainDict[cell2][cell4][cell6][cell8] = concatTerrain;
                }
            });

            return JsonSerializer.Serialize(terrainDict);
        }

        [HttpGet]
        [Route("/[controller]/All")]
        public string GetAllOfflineData() {
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

            foreach (var cell2 in GetCell2Combos()) {
                var place2 = cell2.ToPolygon();
                var placeTest = db.Places.Any(p => p.ElementGeometry.Intersects(place2));
                if (!placeTest)
                    continue;

                terrainDict[cell2] = new ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentDictionary<string, string>>>();
                foreach (var cell4 in GetCellCombos()) {
                    terrainDict[cell2][cell4] = new ConcurrentDictionary<string, ConcurrentDictionary<string, string>>();
                    foreach (var cell6 in GetCellCombos()) {
                        string pluscode6 = cell2 + cell4 + cell6;
                        GeoArea box6 = pluscode6.ToGeoArea();
                        var quickplaces = Place.GetPlaces(box6);
                        if (quickplaces.Count == 0)
                            continue;

                        terrainDict[cell2][cell4][cell6] = new ConcurrentDictionary<string, string>();

                        foreach (var place in quickplaces)
                            if (place.ElementGeometry.Coordinates.Length > 1000)
                                place.ElementGeometry = place.ElementGeometry.Intersection(box6.ToPolygon());

                        Parallel.ForEach(GetCellCombos(), (cell8) => {
                            string pluscode = pluscode6 + cell8;
                            GeoArea box = pluscode.ToGeoArea();
                            var places = Place.GetPlaces(box, quickplaces);
                            if (places.Count == 0)
                                return;

                            places = places.Where(p => p.IsGameElement).ToList();
                            if (places.Count == 0)
                                return;
                            var terrainInfo = AreaStyle.GetAreaDetails(ref box, ref places);
                            var terrainsPresent = terrainInfo.Select(t => t.data.style).Distinct().ToList();

                            if (terrainsPresent.Count > 0) {
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
            foreach (var entry in TagParser.allStyleGroups[style]) {
                if (entry.Value.IsGameElement) {
                    dict.Add(entry.Key, dict.Count + 1);
                }
            }
            return dict;
        }

        static List<string> GetCellCombos() {
            var list = new List<string>(400);
            foreach (var Yletter in OpenLocationCode.CodeAlphabet)
                foreach (var Xletter in OpenLocationCode.CodeAlphabet) {
                    list.Add(String.Concat(Yletter, Xletter));
                }

            return list;
        }

        static List<string> GetCell2Combos() {
            var list = new List<string>(400);
            foreach (var Yletter in OpenLocationCode.CodeAlphabet.Take(9))
                foreach (var Xletter in OpenLocationCode.CodeAlphabet.Take(18)) {
                    list.Add(String.Concat(Yletter, Xletter));
                }

            return list;
        }

        //TODO: test these formats and clean them up to take less space. short-names and all that.
        public class OfflineDataV2
        {
            public List<OfflinePlaceEntry> entries { get; set; }
            public Dictionary<string, int> nameTable { get; set; }
            public int[][] nameIds { get; set; }
        }

        public class OfflinePlaceEntry {
            public int nameTableId { get; set; }
            public int terrainId { get; set; } //which style entry this place is
            public int geoType { get; set; } //1 = point, 2 = line OR hollow shape, 3 = filled shape.
            //public string geometry { get; set; }
            public string Points { get; set; } //alt-take on geometry.
        }

        [HttpGet]
        [Route("/[controller]/V2/{plusCode8}")]
        [Route("/[controller]/V2/{plusCode8}/{styleSet}")]
        public string GetOfflineDataV2(string plusCode8, string styleSet = "mapTiles")
        {
            //Trying a second approach to this. I want a smaller set of data, but I also want to expand whats available in this.
            //This assumes that a server exists, but it lives primarily NOT to draw tiles itself, but to parse data down for a specific game client which does that work.
            //adding: nametable per Cell10, and possibly geometry. This may get limited to the cell8 itself so its self-contained and only whats used gets loaded.
            using var db = new PraxisContext();
            db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            db.ChangeTracker.AutoDetectChangesEnabled = false;

            var addGeo = true;

            //save and load this from the db if we've generated it before.
            var existingData = db.AreaData.FirstOrDefault(a => a.PlusCode == plusCode8 && a.DataKey == "offlineV2");
            if (existingData != null)
                return existingData.DataValue.ToUTF8String();

            var cell8 = plusCode8.ToGeoArea();
            var cell8Poly = cell8.ToPolygon();
            var placeData = Place.GetPlaces(cell8, styleSet: styleSet, dataKey: styleSet);
            int nameCounter = 0; //used to determine nametable key

            List<OfflinePlaceEntry> entries = new List<OfflinePlaceEntry>(placeData.Count);
            Dictionary<string, int> nametable = new Dictionary<string, int>(); //name, id

            foreach(var place in placeData)
            {
                var offline = new OfflinePlaceEntry();
                offline.terrainId = (int)TagParser.allStyleGroups[styleSet][place.StyleName].Id; //Client will need to know what this ID means.
                //Crop this geometry to the Cell8 we're looking at to minimize data sent over, and simplify it to a Cell10 resolution.
                if (addGeo)
                {
                    //TODO: this may need to convert a polygon to a line before calling Intersection if the style only has Stroke items.
                    //TODO: convert points to 0,100 pixel-space coordinates. Subtract min-coord value for the plus code, then value / Cell1res?

                    var min = cell8.Min;
                    place.ElementGeometry = place.ElementGeometry.Intersection(cell8Poly).Simplify(ConstantValues.resolutionCell10);
                    //offline.geometry = place.ElementGeometry.AsText();
                    offline.geoType = place.ElementGeometry.GeometryType == "Point" ? 1 : place.ElementGeometry.GeometryType == "LineString" ? 2 : 3;

                    //NOTE: if I'm locking these geometry items to a tile, I could convert these points in the geometry to integers between 0 and 100, effectively
                    //letting me draw Cell11 pixel-precise points from this info for an 80x100 base-scale image, and would be shorter stringified for JSON vs floats/doubles.
                    offline.Points = string.Join("|", place.ElementGeometry.Coordinates.Select(c => (int)((c.X - min.Longitude) / ConstantValues.resolutionCell11Lon) + "," + ((int)(c.Y - min.Latitude) / ConstantValues.resolutionCell11Lat)));
                }

                //TODO: AreaStyle.GetAreaDetails() style crawl over all listed places to determine names and values?

                var name = TagParser.GetName(place);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    if (!nametable.TryGetValue(name, out var nameid))
                    {
                        nameid = nameCounter;
                        nametable.Add(name, nameid);
                        nameCounter++;
                        //attach to this item.
                    }
                    offline.nameTableId = nameid;
                }
                entries.Add(offline);
            }

            var finalData = new OfflineDataV2();
            finalData.nameTable = nametable;
            finalData.entries = entries;

            string data = JsonSerializer.Serialize(finalData);
            db.AreaData.Add(new DbTables.AreaData() { PlusCode = plusCode8, DataKey = "offlineV2", DataValue = data.ToByteArrayUTF8(), Expiration = DateTime.UtcNow.AddYears(10) });
            db.SaveChanges();

            return data;
        }
    }
}