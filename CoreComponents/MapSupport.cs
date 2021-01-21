using CoreComponents.Support;
using Google.OpenLocationCode;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Prepared;
using NetTopologySuite.Index.IntervalRTree;
using NetTopologySuite.Operation.Buffer;
using Newtonsoft.Json;
using OsmSharp.Tags;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using static CoreComponents.DbTables;
using static CoreComponents.ConstantValues;
using static CoreComponents.Singletons;
using static CoreComponents.Place;
using static CoreComponents.GeometrySupport;
using static CoreComponents.AreaTypeInfo;

namespace CoreComponents
{
    public static class MapSupport
    {
        //TODO:
        //remove this file after confirming i dont need these last 2 commented out blocks.
        

        
        

        //I don't think this is going to be a high-demand function, but I'll include it for performance comparisons.
        //public static string FindPlacesIn11Cell(double x, double y, ref List<MapData> places, bool entireCode = false)
        //{
        //    var box = new GeoArea(new GeoPoint(y, x), new GeoPoint(y + resolutionCell11Lat, x + resolutionCell11Lon));
        //    var entriesHere = GetPlaces(box, places).Where(p => p.AreaTypeId != 13).ToList(); //Excluding admin boundaries from this list.  

        //    if (entriesHere.Count() == 0)
        //        return "";

        //    string area = DetermineAreaPoint(entriesHere);
        //    if (area != "")
        //    {
        //        string olc;
        //        if (entireCode)
        //            olc = new OpenLocationCode(y, x, 11).CodeDigits;
        //        else
        //            olc = new OpenLocationCode(y, x, 11).CodeDigits.Substring(6, 5); //This takes lat, long, Coordinate takes X, Y. This line is correct.
        //        return olc + "|" + area;
        //    }
        //    return "";
        //}

        

        

        //Move this to an admin call? or Place?
        


        
        

        //Unused so far. Keeping as a potential future feature.
        //public static GeoPoint ProxyLocation(double lat, double lon, GeoArea bounds)
        //{
        //    //Treat the user like they're in the real-world location
        //    //Mod their location by the box size, then add that to the minimum to get their new location
        //    var shiftX = lon % bounds.LongitudeWidth;
        //    var shiftY = lat % bounds.LatitudeHeight;

        //    double newLat = bounds.SouthLatitude + shiftY;
        //    double newLon = bounds.WestLongitude + shiftX;

        //    return new GeoPoint(newLat, newLon);
        //}


        

 
        //public static void ExpireCellData(string? plusCode = "", int? MapDataId = 0)
        //{
        //    //TODO: this function.
        //    //Clear out anything generated involving the given area so that it can be regenerated.
        //    //should be able to work out both by PlusCode, and by MapDataId (though this second one will be much trickier.)
        //    var db = new PraxisContext();
        //    db.MapTiles.RemoveRange(db.MapTiles.Where(m => m.PlusCode == plusCode));
        //    db.SaveChanges();
        //}
    }
}
