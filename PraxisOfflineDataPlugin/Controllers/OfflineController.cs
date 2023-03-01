using Google.OpenLocationCode;
using Microsoft.AspNetCore.Mvc;
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

            var db = new PraxisContext();
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
                            terrainDict[cell2][cell4].TryRemove(cell6, out var ignore);
                    }
                    if (terrainDict[cell2][cell4].IsEmpty)
                        terrainDict[cell2].TryRemove(cell4, out var ignore);
                }
                if (terrainDict[cell2].IsEmpty)
                    terrainDict[cell2].TryRemove(cell2, out var ignore);

                //NOTE and TODO: if I want to save files per Cell2, here is where I should write terrainDict, then remove the current Cell2 entry and loop.
            }

            return JsonSerializer.Serialize(terrainDict);
        }

        private static Dictionary<string, int> GetTerrainIndex() //TODO make style a parameter
        {
            var dict = new Dictionary<string, int>();
            foreach (var entry in TagParser.allStyleGroups["mapTiles"]) {
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
    }
}