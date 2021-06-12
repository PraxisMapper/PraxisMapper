using CoreComponents;
using CoreComponents.Support;
using NetTopologySuite.Geometries;
using OsmSharp;
using OsmSharp.Streams;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using static CoreComponents.DbTables;
using static CoreComponents.Place;
using static CoreComponents.Singletons;

namespace Larry
{
    //PbfOperations is for functions that do processing on a PBF file to create some kind of output.
    public static class PbfOperations
    {
        //This is kind of obsolete, but only because I should be able to process these larger files normally now.
        //public static void ExtractAreasFromLargeFile(string filename)
        //{
        //    //This should refer to a list of relations that cross multiple extract files, to get a more accurate set of data in game.
        //    //Starting with North America, will test later on global data
        //    //Should start with big things
        //    //Great lakes, major rivers, some huge national parks. Oceans are important for global data.
        //    //Rough math suggests that this will take 103 minutes to skim planet-latest.osm.pbf per pass.
        //    string outputFile = ParserSettings.JsonMapDataFolder + "LargeAreas" + (ParserSettings.UseHighAccuracy ? "-highAcc" : "-lowAcc") + ".json";

        //    var manualRelationId = new List<long>() {
        //        //Great Lakes:
        //        4039900, //Lake Erie
        //        1205151, //Lake Huron
        //        1206310, //lake ontario
        //        1205149, //lake michigan --not valid geometry?
        //        4039486, //lake superior
        //        //Admin boundaries:
        //        //Any relation with admin_level == 2 is a country.
        //        148838, //US Admin bounds
        //        9331155, //48 Contiguous US states
        //        1428125, //Canada
        //        //EU?
        //        //Which other countries do I have divided down into state/provinces?
        //        //UK
        //        //Germany
        //        //France
        //        //Russia
        //        //others
        //        //Oceans don't exist in OSM because their specific boundaries are poorly defined.
        //        //TODO: multi-state or multi-nation rivers.
        //        2182501, //Ohio River
        //        1756854, //Mississippi river --failed to get a polygon?
        //        //other places:
        //        //yellowstone?
        //        //grand canyon?
        //    };

        //    var stream = new FileStream(filename, FileMode.Open);
        //    var source = new PBFOsmStreamSource(stream);
        //    File.Delete(outputFile); //Clear out any existing entries.

        //    File.AppendAllLines(outputFile, new List<String>() { "[" });
        //    var countryFilter = source.Where(s => s.Type == OsmGeoType.Relation &&
        //        (manualRelationId.Contains(s.Id.Value)
        //        //This clause adds countries, but it errored out on me somwhere after about 10 hours of running on planet-latest.
        //        || s.Tags.Any(t => t.Key == "admin_level" && t.Value == "2")) //Countries
        //        ).ShowProgress(); //forces the IEnumerable back to a stream.
        //    var rs = PbfFileParser.ProcessFileCoreV4(countryFilter, true, outputFile);

        //    Log.WriteLog("Large Areas saved to file at " + DateTime.Now);
        //}
    }
}
