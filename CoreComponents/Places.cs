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
        public static List<DbTables.Place> GetPlaces(GeoArea area, List<DbTables.Place> source = null, double filterSize = 0, string styleSet = "mapTiles",
            bool skipTags = false, bool skipGeometry = false, string tagKey = null, string tagValue = null, string dataKey = null, string dataValue = null, string skipType = null)
        {
            //The flexible core of the lookup functions. Takes an area, returns results that intersect from Source. If source is null, looks into the DB.
            //Intersects is the only indexable function on a geography column I would want here. Distance and Equals can also use the index, but I don't need those in this app.
            IQueryable<DbTables.Place> queryable;
            PraxisContext db = null;


            //NOTE: with the EF Core setup, this returns 1 row per tag/PlaceData entry, and each of those will have the elementGeometry
            //attached. Pulling in a detailed area with lots of tags may be disproportionately slow.

            //TODO: can I use Simplify in EFCore db-side? Probably not but worth checking. Might be OK to send over a reduced version
            //where the precision is the 1-pixel size so areas with TONS of points get sent over as far fewer.
            List<DbTables.Place> places;
            if (source == null)
            {
                db = new PraxisContext();
                db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
                db.ChangeTracker.AutoDetectChangesEnabled = false;
                db.Database.SetCommandTimeout(new TimeSpan(0, 5, 0));
                queryable = db.Places;

            }
            else
                queryable = source.AsQueryable();

            queryable = queryable.Include(q => q.PlaceData); //With pre-tagging enforced you can't skip this.
            
            if (dataKey != null)
                queryable = queryable.Where(p => p.PlaceData.Any(t => t.DataKey == dataKey));

            if (dataValue != null)
            {
                byte[] dv = dataValue.ToByteArrayUTF8();
                queryable = queryable.Where(p => p.PlaceData.Any(t => t.DataValue == dv));
            }

            if (skipType != null)
                queryable = queryable.Where(p => !p.PlaceData.Any(t => t.DataKey == skipType));

            if (!skipTags)
            {
                queryable = queryable.Include(q => q.Tags);

                if (tagKey != null)
                    queryable = queryable.Where(p => p.Tags.Any(t => t.Key == tagKey));

                if (tagValue != null)
                    queryable = queryable.Where(p => p.Tags.Any(t => t.Value == tagValue));
            }

            var paddedArea = GeometrySupport.MakeBufferedGeoArea(area);
            var location = paddedArea.ToPolygon();
            
            //Splitting this into 2 Where() clauses is a huge improvement in speed on large areas. The spatial index is the slower of the 2, so force it to go 2nd
            queryable = queryable.Where(md => md.DrawSizeHint >= filterSize).Where(md => location.Intersects(md.ElementGeometry));

            if (skipGeometry)
                queryable = queryable.Select(q => new DbTables.Place() { DrawSizeHint = q.DrawSizeHint, Id = q.Id, PrivacyId = q.PrivacyId, SourceItemID = q.SourceItemID, SourceItemType = q.SourceItemType, Tags = q.Tags });

            places = queryable.ToList();
            places = places.OrderByDescending(p => p.DrawSizeHint).ToList(); //Sort server-side on this to make bigger queries faster.
            TagParser.ApplyTags(places, styleSet);

            if (db != null)
            {
                db.ChangeTracker.Clear();
                db.Dispose();
            }
        
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

        public static DbTables.Place GetPlace(Guid privacyId, string styleSet = "mapTiles")
        {
            var db = new PraxisContext();
            db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var place = db.Places.Include(q => q.Tags).Include(q => q.PlaceData).FirstOrDefault(p => p.PrivacyId == privacyId);
            TagParser.ApplyTags(place, styleSet);
            return place;
        }

        public static DbTables.Place GetPlace(string privacyId)
        {
            return GetPlace(new Guid(privacyId));
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
            var location = area.ToPolygon();
            if (source == null)
            {
                using var db = new PraxisContext();
                db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
                db.ChangeTracker.AutoDetectChangesEnabled = false;
                return db.Places.Include(p => p.Tags).Any(md => md.ElementGeometry.Intersects(location) && !md.Tags.Any(t => t.Key == "bgwater")); //Exclude oceans from bounds checks.
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
            double SouthLimit;
            double NorthLimit;
            double WestLimit;
            double EastLimit;

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
        public static List<DbTables.Place> GetPlacesByStyle(string type, GeoArea area, List<DbTables.Place> places = null, string styleSet = "mapTiles")
        {
            if (places == null)
                places = GetPlaces(area, styleSet: styleSet);
            return places.Where(p => p.StyleName == type).ToList();
        }

        public static List<DbTables.Place> GetPlacesByData(string key, string value, List<DbTables.Place> places = null, bool skipGeometry = false)
        {
            //A slightly different setup because this doesn't search on the spatial index.

            byte[] dbValue = value.ToByteArrayUTF8();
            if (places == null)
            {
                var db = new PraxisContext();
                db.Database.SetCommandTimeout(600);
                IQueryable<DbTables.Place> query = db.Places.Include(p => p.Tags).Include(p => p.PlaceData);
                query = query.Where(p => p.PlaceData.Any(d => d.DataKey == key && d.DataValue == dbValue));
                if (skipGeometry)
                    query = query.Select(q => new DbTables.Place() { DrawSizeHint = q.DrawSizeHint, Id = q.Id, PrivacyId = q.PrivacyId, SourceItemID = q.SourceItemID, SourceItemType = q.SourceItemType, Tags = q.Tags });

                return query.ToList();
            }
            else
            {
                return places.Where(p => p.PlaceData.Any(d => d.DataKey == key && d.DataValue == dbValue)).ToList();
            }
        }

        public static List<DbTables.Place> GetPlacesByTags(string key, string value, List<DbTables.Place> places = null, bool skipGeometry = false)
        {
            //A slightly different setup because this doesn't search on the spatial index.
            if (places == null)
            {
                var db = new PraxisContext();
                db.Database.SetCommandTimeout(600);
                IQueryable<DbTables.Place> query = db.Places.Include(p => p.Tags).Include(p => p.PlaceData);
                query = query.Where(p => p.Tags.Any(d => d.Key == key && d.Value == value));
                if (skipGeometry)
                    query = query.Select(q => new DbTables.Place() { DrawSizeHint = q.DrawSizeHint, Id = q.Id, PrivacyId = q.PrivacyId, SourceItemID = q.SourceItemID, SourceItemType = q.SourceItemType, Tags = q.Tags });

                return query.ToList();
            }
            else
            {
                return places.Where(p => p.Tags.Any(d => d.Key == key && d.Value == value)).ToList();
            }
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
            using var db = new PraxisContext();
            db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var distancePoly = ntsPoint.Buffer(distance);
            var dbresults = db.Places
                .Include(o => o.Tags)
                .Include(o => o.PlaceData)
                .Where(o => o.ElementGeometry.Intersects(distancePoly))
                .ToList();

            List<Tuple<double, DbTables.Place>> results = dbresults.Select(o => Tuple.Create(o.ElementGeometry.Distance(ntsPoint), o)).OrderByDescending(o => o.Item1).ToList();
            return results;
        }

        public static List<DbTables.Place> FindAnyTargetPlaces(string plusCode, double distanceMinMeters, double distanceMaxMeters, string styleSet = "suggestedGameplay")
        {
            //Finds all places that fall between a minimum and maximum distance from the starting point, without additional criteria.
            //TO PONDER: is this a good place to try and upgrade points to buildings?

            var maxArea = plusCode.ToGeoArea().ToPolygon().Buffer(distanceMaxMeters.DistanceMetersToDegreesLat()); //square, because the geoArea is square before buffering.
            var minArea = plusCode.ToGeoArea().ToPolygon().Buffer(distanceMinMeters.DistanceMetersToDegreesLat());


            var db = new PraxisContext(); //NOTE: MariaDB doesn't support CoveredBy or Covers, which I would prefer but I'll use what works.
            //This way is ~2-3x faster than calling GetPlaces for this logic.
            var places = db.Places.Include(p => p.PlaceData).Include(p => p.Tags).Where(p => p.ElementGeometry.Intersects(maxArea) && !p.ElementGeometry.Intersects(minArea)).ToList();
            places = TagParser.ApplyTags(places, styleSet);
            places = places.Where(p => p.IsGameElement).ToList();

            return places;
        }

        public static List<DbTables.Place> FindGoodTargetPlaces(string plusCode, double distanceMinMeters, double distanceMaxMeters, string styleSet)
        {
            //Find target places for a visit that are also inside another gameplay element place
            //EX: visit a Historic marker at a NatureReserve.
            //This theory ends up not being as practical as I'd hoped. This MOSTLY picks up on retail entries, since 
            //most of those are POINTS inside a larger shopping center thats also tagged Retail.
            //So this may get removed shortly in favor of something more reasonable and doing extra processing after I have the list.
            //TODO: double check this, but without limiting it to Points. May have better luck checking for Ways in Ways/Relations?

            var allPlaces = FindAnyTargetPlaces(plusCode, distanceMinMeters, distanceMaxMeters, styleSet);
            //trying to see if this works better without limiting to points? TODO test this to see if its better
            //var targetPlaces = allPlaces.Where(a => a.IsGameElement).ToList();
            var possibleGood = allPlaces.Where(t => allPlaces.Any(tt => t.Id != tt.Id && tt.ElementGeometry.Covers(t.ElementGeometry))).ToList();
            //var possibleParents = allPlaces.Except(targetPoints).Where(a => a.IsGameElement).OrderBy(a => a.ElementGeometry.Area);

            return possibleGood;
            //return allPlaces;

            //List<DbTables.Place> results = new List<DbTables.Place>();
            //foreach(var p in possibleParents)
            //{
            //    //I have briefly considered not allowing these target point to be in the same style as their parent, but 
            //    //there are likely more cases where I want that to be allowed (Historic in historic, tourist in tourist, culture in culture)
            //    //than there are cases where I dont (park in park, water in water, etc).
            //    results.AddRange(targetPoints.Where(t => p.ElementGeometry.Covers(t.ElementGeometry)));
            //}

            //return results.Distinct().ToList();
        }

        public static List<DbTables.Place> FindParentTargetPlaces(string plusCode, double distanceMinMeters, double distanceMaxMeters, string styleSet)
        {
            //Find target places for a visit that contain other places to visit.
            //EX: a university may contain arts&culture points to visit.

            var allPlaces = FindAnyTargetPlaces(plusCode, distanceMinMeters, distanceMaxMeters, styleSet);
            var targetPoints = allPlaces.Where(a => a.IsGameElement && a.ElementGeometry.GeometryType == "Point").ToList();
            var possibleParents = allPlaces.Except(targetPoints).Where(a => a.IsGameElement).OrderBy(a => a.ElementGeometry.Area).Take(10);

            List<DbTables.Place> results = new List<DbTables.Place>();
            foreach (var p in possibleParents)
            {
                //
                if (targetPoints.Any(t => p.ElementGeometry.Covers(t.ElementGeometry) && !possibleParents.Any(pp => pp.ElementGeometry.Covers(p.ElementGeometry))))
                    results.Add(p);
            }

            return results.Distinct().ToList();
        }

        public static List<DbTables.Place> FindPlacesInPlace(DbTables.Place place, string styleSet = "mapTiles")
        {
            var area = place.ElementGeometry.Envelope.ToGeoArea();
            var allNearby = GetPlaces(area, styleSet: styleSet);

            var goodTargets = allNearby.Where(a => a.IsGameElement && place.ElementGeometry.Intersects(a.ElementGeometry)).ToList();

            return goodTargets;
        }


        /// <summary>
        /// Returns a random 10-digit PlusCode inside the server boundaries. Boundaries are a square around the actual geometry, so these may still occur in uninteresting areas.
        /// </summary>
        /// <param name="bounds"></param>
        /// <returns></returns>
        public static string RandomPoint(ServerSetting bounds)
        {
            var ranLat = (Random.Shared.NextDouble() * (bounds.NorthBound - bounds.SouthBound)) + bounds.SouthBound;
            var ranLon = (Random.Shared.NextDouble() * (bounds.EastBound - bounds.WestBound)) + bounds.WestBound;

            var olc = new OpenLocationCode(ranLat, ranLon);
            return olc.CodeDigits;
        }

        public static void PreTag(DbTables.Place place)
        {
            //tag this place.
            foreach (var style in TagParser.allStyleGroups.Where(g => g.Key != "outlines" && g.Key != "paintTown"))
            {
                TagParser.ApplyTags(place, style.Key);
                bool update = false;
                
                if (place.StyleName != "unmatched" && place.StyleName != "background")
                {
                    PlaceData info = place.PlaceData.FirstOrDefault(d => d.DataKey == style.Key);
                    if (info == null)
                    {
                        info = new PlaceData() { DataKey = style.Key, DataValue = place.StyleName.ToByteArrayUTF8() };
                        place.PlaceData.Add(info);
                    }
                    else
                    {
                        info.DataValue = place.StyleName.ToByteArrayUTF8();
                    }
                }
            }
        }

        public static void UpdateChanges(DbTables.Place current, DbTables.Place newValues, PraxisContext db)
        {
            var entityData = db.Entry(current);
            if (!current.ElementGeometry.EqualsTopologically(newValues.ElementGeometry))
            {
                entityData.Property(p => p.ElementGeometry).CurrentValue = newValues.ElementGeometry;
                entityData.Property(p => p.DrawSizeHint).CurrentValue = newValues.DrawSizeHint;
            }
            if (!(current.Tags.Count == newValues.Tags.Count && current.Tags.All(t => newValues.Tags.Any(tt => tt.Equals(t)))))
            {
                entityData.Collection(p => p.Tags).CurrentValue = newValues.Tags;
            }

            var entries = current.PlaceData;
            if (entries == null)
                entityData.Collection(p => p.PlaceData).CurrentValue = newValues.PlaceData;
            else
            {
                var epd = new List<PlaceData>();
                foreach (var data in newValues.PlaceData)
                {
                    var oldPreTag = current.PlaceData.FirstOrDefault(p => p.DataKey == data.DataKey);
                    if (oldPreTag == null)
                        epd.Add(data);
                    else
                        if (oldPreTag.DataValue != data.DataValue)
                        oldPreTag.DataValue = data.DataValue;
                }

                if (epd.Count > 0)
                {
                    epd.AddRange(current.PlaceData.Where(d => !epd.Any(ee => ee.DataKey == d.DataKey)));
                    entityData.Collection(p => p.PlaceData).CurrentValue = epd;
                }
            }
        }
    }
}
