using Google.OpenLocationCode;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using PraxisCore.Support;
using System;
using System.Collections.Generic;
using System.Linq;
using static PraxisCore.DbTables;

namespace PraxisCore
{
    /// <summary>
    /// Places are OpenStreetMap Relations, Ways, or Nodes with tags of interest. DbTables.Place holds the actual data.
    /// </summary>
    public static class Place
    {
        //Places will be the name for interactible or important areas on the map. Generally, a Place in the DB.
        //(vs Area, which is a PlusCode of any size)

        //All elements in the table with Geometry will be valid, and the TagParser rules will determine which ones are game elements
        //this allows it to be customized much easier, and changed on the fly without reloading data.

        /// <summary>
        /// The core for pulling in locations from PraxisMapper. Can do a new search on an existing list of Place or pulls from the database if none is provided. Adds padding as set from config automatically.
        /// </summary>
        /// <param name="area">The GeoArea to intersect locations against, and include ones that do. </param>
        /// <param name="source">Null to load from the database, or a List of Places to narrow down</param>
        /// <param name="filterSize">Removes any areas with a length or perimeter over this value. Defaults to 0 to include everything.</param>
        /// <param name="styleSet">A style set to run the found locations through for identification.</param>
        /// <param name="skipTags">If true, skips over tagging elements. A performance boost when you have a List to narrow down already.</param>
        /// <param name="skipGeometry">If true, elementGeometry will not be loaded from the database. Defaults to false.</param>
        /// <returns>A list of Places that intersect the area, have a perimter greater than or equal to filtersize.</returns>
        public static List<DbTables.Place> GetPlaces(GeoArea area, List<DbTables.Place> source = null, double filterSize = 0, string styleSet = "mapTiles", bool skipTags = false, bool skipGeometry = false)
        {
            //The flexible core of the lookup functions. Takes an area, returns results that intersect from Source. If source is null, looks into the DB.
            //Intersects is the only indexable function on a geography column I would want here. Distance and Equals can also use the index, but I don't need those in this app.
            IQueryable<DbTables.Place> queryable;

            List<DbTables.Place> places;
            if (source == null)
            {
                var db = new PraxisContext();
                db.ChangeTracker.AutoDetectChangesEnabled = false;
                db.Database.SetCommandTimeout(new TimeSpan(0, 5, 0));
                queryable = db.Places;
            }
            else
                queryable = source.AsQueryable();

            if (!skipTags)
                queryable = queryable.Include(q => q.Tags).Include(q => q.PlaceData);

            var paddedArea = GeometrySupport.MakeBufferedGeoArea(area);
            var location = Converters.GeoAreaToPolygon(paddedArea); 
            queryable = queryable.Where(md => location.Intersects(md.ElementGeometry) && md.DrawSizeHint >= filterSize).OrderByDescending(w => w.ElementGeometry.Area).ThenByDescending(w => w.ElementGeometry.Length);

            if (skipGeometry)
                queryable = queryable.Select(q => new DbTables.Place() { DrawSizeHint = q.DrawSizeHint, Id = q.Id, PrivacyId = q.PrivacyId, SourceItemID = q.SourceItemID, SourceItemType = q.SourceItemType, Tags = q.Tags });

            places = queryable.ToList();
            TagParser.ApplyTags(places, styleSet);
            return places;
        }

        /// <summary>
        /// A shortcut function to pull GetPlaces info against an ImageStats objects.
        /// </summary>
        /// <param name="stats">the ImageStats containing some of the other parameters to pass through</param>
        /// <param name="source">Null to load from the database, or a List of Places to narrow down</param>
        /// <param name="styleSet">A TagParser style set to run the found locations through for identification.</param>
        /// <param name="skipTags">If true, skips over tagging elements. A performance boost when you have a List to narrow down already.</param>
        /// /// <param name="skipGeometry">If true, elementGeometry will not be loaded from the database. Defaults to false.</param>
        /// <returns>A list of Places that intersect the area, have a perimter greater than or equal to filtersize.</returns>
        public static List<DbTables.Place> GetPlaces(ImageStats stats, List<DbTables.Place> source = null, string styleSet = "mapTiles", bool skipTags = false, bool skipGeometry = false)
        {
            return GetPlaces(stats.area, source, stats.filterSize, styleSet, skipTags, skipGeometry);
        }

        //Note: This should have the padding added to area before this is called, if checking for tiles that need regenerated.
        /// <summary>
        /// Checks if places existing in the DB for the given area. Will almost always return true, given admin boundaries are loaded into a database. Used for finding server boundaries and if an area needs maptiles drawn.
        /// </summary>
        /// <param name="area">The area to check for elements</param>
        /// <param name="source">an optional list to use instead of loading from the database.</param>
        /// <returns>true if any Places intersect the given GeoArea, false if not.</returns>
        public static bool DoPlacesExist(GeoArea area, List<DbTables.Place> source = null)
        {
            var location = Converters.GeoAreaToPolygon(area);
            if (source == null)
            {
                var db = new PraxisContext();
                return db.Places.Any(md => md.ElementGeometry.Intersects(location));
            }
            return source.Any(md => md.ElementGeometry.Intersects(location));
        }

        /// <summary>
        /// Scans the database to determine the outer boundaries of locations in it.
        /// </summary>
        /// <param name="resolution">How many degrees to scan at a time. Must be smaller than 1.</param>
        /// <returns>a GeoArea representing the bounds of the server's location at the resolution given.</returns>
        public static GeoArea DetectServerBounds(double resolution)
        {
            //Auto-detect what the boundaries are for the database's data set.
            //NOTE: with the Aleutian islands, the USA is considered as wide as the entire map. These sit on both sides of the meridian.
            //These 2 start in the opposite corners, to make sure the replacements are correctly detected.
            double SouthLimit = 360;
            double NorthLimit = -360;
            double WestLimit = 360;
            double EastLimit = -360;

            double scanRes = 1; //1 degree.
            //This is now a 2-step process for speed. The first pass runs at 1 degree intervals for speed, then drops to the given resolution for precision.
            var northscanner = new GeoArea(new GeoPoint(90 - scanRes, -180), new GeoPoint(90, 180));
            while (!DoPlacesExist(northscanner) && northscanner.SouthLatitude > -90)
                northscanner = new GeoArea(new GeoPoint(northscanner.SouthLatitude - scanRes, -180), new GeoPoint(northscanner.NorthLatitude - scanRes, 180));
            northscanner = new GeoArea(new GeoPoint(northscanner.SouthLatitude - resolution, -180), new GeoPoint(northscanner.NorthLatitude - resolution, 180));
            while (!DoPlacesExist(northscanner))
                northscanner = new GeoArea(new GeoPoint(northscanner.SouthLatitude - resolution, -180), new GeoPoint(northscanner.NorthLatitude - resolution, 180));
            NorthLimit = northscanner.NorthLatitude;

            var southscanner = new GeoArea(new GeoPoint(-90, -180), new GeoPoint(-90 + scanRes, 180));
            while (!DoPlacesExist(southscanner))
                southscanner = new GeoArea(new GeoPoint(southscanner.SouthLatitude + scanRes, -180), new GeoPoint(southscanner.NorthLatitude + scanRes, 180));
            southscanner = new GeoArea(new GeoPoint(southscanner.SouthLatitude + resolution, -180), new GeoPoint(southscanner.NorthLatitude + resolution, 180));
            while (!DoPlacesExist(southscanner))
                southscanner = new GeoArea(new GeoPoint(southscanner.SouthLatitude + resolution, -180), new GeoPoint(southscanner.NorthLatitude + resolution, 180));
            SouthLimit = southscanner.SouthLatitude;

            var westScanner = new GeoArea(new GeoPoint(-90, -180), new GeoPoint(90, -180 + scanRes));
            while (!DoPlacesExist(westScanner))
                westScanner = new GeoArea(new GeoPoint(-90, westScanner.WestLongitude + scanRes), new GeoPoint(90, westScanner.EastLongitude + scanRes));
            westScanner = new GeoArea(new GeoPoint(-90, westScanner.WestLongitude + resolution), new GeoPoint(90, westScanner.EastLongitude + resolution));
            while (!DoPlacesExist(westScanner))
                westScanner = new GeoArea(new GeoPoint(-90, westScanner.WestLongitude + resolution), new GeoPoint(90, westScanner.EastLongitude + resolution));
            WestLimit = westScanner.WestLongitude;

            var eastscanner = new GeoArea(new GeoPoint(-90, 180 - scanRes), new GeoPoint(90, 180));
            while (!DoPlacesExist(eastscanner))
                eastscanner = new GeoArea(new GeoPoint(-90, eastscanner.WestLongitude - scanRes), new GeoPoint(90, eastscanner.EastLongitude - scanRes));
            eastscanner = new GeoArea(new GeoPoint(-90, eastscanner.WestLongitude - resolution), new GeoPoint(90, eastscanner.EastLongitude - resolution));
            while (!DoPlacesExist(eastscanner))
                eastscanner = new GeoArea(new GeoPoint(-90, eastscanner.WestLongitude - resolution), new GeoPoint(90, eastscanner.EastLongitude - resolution));
            EastLimit = eastscanner.EastLongitude;

            return new GeoArea(new GeoPoint(SouthLimit, WestLimit), new GeoPoint(NorthLimit, EastLimit));
        }

        //This is for getting all places that have a specific TagParser style/match. Admin boundaries, parks, etc.
        /// <summary>
        /// Pull in all elements from a list that have a given style. 
        /// </summary>
        /// <param name="type">the name of the style to select elements for</param>
        /// <param name="area">the GeoArea to select elements from.</param>
        /// <param name="places">A list of OSM Elements to search. If null, loaded from the database based on the area provided.</param>
        /// <returns>a list of OSM Elements with the requested style in the given area.</returns>
        public static List<DbTables.Place> GetPlacesByStyle(string type, GeoArea area, List<DbTables.Place> places = null) //TODO this should take StyleSet as a param and pass that to GetPlaces
        {
            if (places == null)
                places = GetPlaces(area);
            return places.Where(p => p.StyleName == type).ToList();
        }

        /// <summary>
        /// Returns a Tuple of Places and their distance (in degrees) from the given PlusCode, ordered by the minimum distance between the 2.
        /// </summary>
        /// <param name="plusCode">The PlusCode to search from. Uses the center of the PlusCode </param>
        /// <param name="distance">The distance in degrees to use as the basis for the search</param>
        /// <returns></returns>
        public static List<Tuple<double, DbTables.Place>> GetNearbyPlaces(string plusCode, double distance)
        {
            return GetNearbyPlaces(plusCode.ToGeoArea().ToPoint(), distance);
        }

        /// <summary>
        /// Returns a Tuple of Places and their distance (in degrees) from the given point, ordered by the minimum distance between the 2.
        /// </summary>
        /// <param name="ntsPoint">the Point to use when searching.</param>
        /// <param name="distance">The distance in degrees to use as the basis for the search</param>
        /// <returns></returns>
        public static List<Tuple<double, DbTables.Place>> GetNearbyPlaces(Point ntsPoint, double distance)
        {
            //This is the fastest way to do this. The DB takes much longer to calculate distances, so we just check what intersects the distance we were looking for,
            //then calculate the distance on all of those elements
            var db = new PraxisContext();
            var distancePoly = ntsPoint.Buffer(distance);
            var dbresults = db.Places
                .Include(o => o.Tags)
                .Include(o => o.PlaceData)
                .Where(o => o.ElementGeometry.Intersects(distancePoly))
                .ToList();

            List<Tuple<double, DbTables.Place>> results = dbresults.Select(o => Tuple.Create(o.ElementGeometry.Distance(ntsPoint), o)).OrderByDescending(o => o.Item1).ToList();
            return results;
        }

        public static string RandomPoint(ServerSetting bounds)
        {
            var ranLat = (Random.Shared.NextDouble() * (bounds.NorthBound - bounds.SouthBound)) + bounds.SouthBound;
            var ranLon = (Random.Shared.NextDouble() * (bounds.EastBound - bounds.WestBound)) + bounds.WestBound;

            var olc = new OpenLocationCode(ranLat, ranLon);
            return olc.CodeDigits;
        }
    }
}
