using CoreComponents;
using CoreComponents.Support;
using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;
using System.Linq;
using static CoreComponents.Singletons;

namespace Larry
{
    //TODO: look into this class. It may be made redundant by using OsmSharp.Geo and the V4 import logic.
    //YEP. This class can largely be replaced with OsmSharp.Geo.FeatureInterpreter.
    public static class GeometryHelper
    {
        public static Geometry GetGeometryFromWays(List<WayData> shapeList, OsmSharp.Relation r)
        {
            //A common-ish case looks like the outer entries are lines that join together, and inner entries are polygons.
            //Let's see if we can build a polygon (or more, possibly)
            List<Coordinate> possiblePolygon = new List<Coordinate>();
            //from the first line, find the line that starts with the same endpoint (or ends with the startpoint, but reverse that path).
            //continue until a line ends with the first node. That's a closed shape.

            List<Polygon> existingPols = new List<Polygon>();
            List<Polygon> innerPols = new List<Polygon>();

            if (shapeList.Count == 0)
            {
                Log.WriteLog("Relation " + r.Id + " " + Place.GetPlaceName(r.Tags) + " has 0 ways in shapelist", Log.VerbosityLevels.High);
                return null;
            }

            //Separate sets
            var innerEntries = r.Members.Where(m => m.Role == "inner").Select(m => m.Id).ToList(); //these are almost always closed polygons.
            var outerEntries = r.Members.Where(m => m.Role == "outer").Select(m => m.Id).ToList();
            var innerPolys = new List<WayData>();

            if (innerEntries.Count() + outerEntries.Count() > shapeList.Count)
            {
                Log.WriteLog("Relation " + r.Id + " " + Place.GetPlaceName(r.Tags) + " is missing Ways, odds of success are low.", Log.VerbosityLevels.High);
            }

            //Not all ways are tagged for this, so we can't always rely on this.
            if (outerEntries.Count > 0)
                shapeList = shapeList.Where(s => outerEntries.Contains(s.id)).ToList();

            if (innerEntries.Count > 0)
            {
                innerPolys = shapeList.Where(s => innerEntries.Contains(s.id)).ToList();
                while (innerPolys.Count() > 0)
                    innerPols.Add(GetShapeFromLines(ref innerPolys));
            }

            //Remove any closed shapes first from the outer entries.
            var closedShapes = shapeList.Where(s => s.nds.First().id == s.nds.Last().id).ToList();
            foreach (var cs in closedShapes)
            {
                if (cs.nds.Count() > 3) // if SimplifyAreas is true, this might have been a closedShape that became a linestring or point from this.
                {
                    shapeList.Remove(cs);
                    existingPols.Add(factory.CreatePolygon(cs.nds.Select(n => new Coordinate(n.lon, n.lat)).ToArray()));
                }
                else
                    Log.WriteLog("Invalid closed shape found: " + cs.id);
            }

            while (shapeList.Count() > 0)
                existingPols.Add(GetShapeFromLines(ref shapeList)); //only outers here.

            existingPols = existingPols.Where(e => e != null).ToList();

            if (existingPols.Count() == 0)
            {
                Log.WriteLog("Relation " + r.Id + " " + Place.GetPlaceName(r.Tags) + " has no polygons and no lines that make polygons. Is this relation supposed to be an open line?", Log.VerbosityLevels.High);
                return null;
            }

            if (existingPols.Count() == 1)
            {
                //remove inner polygons
                var returnData = existingPols.First();
                foreach (var ir in innerPolys)
                {
                    if (ir.nds.First().id == ir.nds.Last().id)
                    {
                        var innerP = factory.CreateLineString(Converters.WayToCoordArray(ir));
                        returnData.InteriorRings.Append(innerP);
                    }
                }
                return returnData;
            }

            //return a multipolygon instead.
            Geometry multiPol = factory.CreateMultiPolygon(existingPols.Distinct().ToArray());
            if (innerPols.Count() > 0)
            {
                var innerMultiPol = factory.CreateMultiPolygon(innerPols.Where(ip => ip != null).Distinct().ToArray());
                try
                {
                    multiPol = multiPol.Difference(innerMultiPol);
                }
                catch (Exception ex)
                {
                    Log.WriteLog("Relation " + r.Id + " Error trying to pull difference from inner and outer polygons:" + ex.Message);
                }
            }
            return multiPol;
        }

        public static Geometry GetGeometryFromCompleteWays(OsmSharp.Complete.CompleteRelation r)
        {
            //A common-ish case looks like the outer entries are lines that join together, and inner entries are polygons.
            //Let's see if we can build a polygon (or more, possibly)
            List<Coordinate> possiblePolygon = new List<Coordinate>();
            //from the first line, find the line that starts with the same endpoint (or ends with the startpoint, but reverse that path).
            //continue until a line ends with the first node. That's a closed shape.

            //List<Polygon> existingPols = new List<Polygon>();
            List<Polygon> innerPols = new List<Polygon>();
            List<Polygon> outerPols = new List<Polygon>();

            if (r.Members.Length == 0)
            {
                Log.WriteLog("Relation " + r.Id + " " + Place.GetPlaceName(r.Tags) + " has 0 members", Log.VerbosityLevels.High);
                return null;
            }

            //Separate sets
            var innerEntries = r.Members.Where(m => m.Role == "inner" && m.Member.Type == OsmSharp.OsmGeoType.Way).Select(m => (OsmSharp.Complete.CompleteWay)m.Member).ToList(); //these are almost always closed polygons.
            var outerEntries = r.Members.Where(m => m.Role == "outer" && m.Member.Type == OsmSharp.OsmGeoType.Way).Select(m => (OsmSharp.Complete.CompleteWay)m.Member).ToList(); //Some outer roles aren't tagged.
            //Not all relations tag members correctly, so if there's none tagged make them outer.

            foreach(var ie in innerEntries)
            {
                var coords = Converters.CompleteWayToCoordArray(ie);
                if (ie.Nodes.First().Id == ie.Nodes.Last().Id)
                    //this is a loop, make a polygon.
                    innerPols.Add(factory.CreatePolygon(coords));
                else
                    Log.WriteLog("Found an inner way that wasn't a polygon. Add support for inner linestrings to be joined into a polygon.");
            }

            List<long> waysToRemove = new List<long>();
            foreach (var oe in outerEntries)
            {
                //If it's a closed shape, make it a polygon
                //if it's a line, try to connect it to other lines to make a polygon. If not, its a linestring.

                //todo move logic in here.
                var coords = Converters.CompleteWayToCoordArray(oe);
                if (oe.Nodes.First().Id == oe.Nodes.Last().Id)
                //this is a loop, make a polygon.
                {
                    outerPols.Add(factory.CreatePolygon(coords));
                    waysToRemove.Add(oe.Id);   
                }
                //else
                //{
                //    existingPols.Add(GetShapeFromLines()); //only outers here.
                //}
            }
            foreach(var wtr in waysToRemove)
                outerEntries.Remove(outerEntries.Where(oe => oe.Id == wtr).First());

            //now make lines into polygons where needed.
            while (outerEntries.Count > 0)
                outerPols.Add(GetShapeFromLines(ref outerEntries)); //TODO: i still think GetShapeFromLines needs some fixes.

            //var innerPolys = new List<OsmSharp.Complete.CompleteWay>();

            //if (innerEntries.Count() + outerEntries.Count() > shapeList.Count)
            //{
            //    Log.WriteLog("Relation " + r.Id + " " + Place.GetPlaceName(r.Tags) + " is missing Ways, odds of success are low.", Log.VerbosityLevels.High);
            //}

            //Not all ways are tagged for this, so we can't always rely on this.
            //if (outerEntries.Count() > 0)
            //shapeList = shapeList.Where(s => outerEntries.Any(o => o.Id == s.Id)).ToList();

            //if (innerEntries.Count() > 0) //This doesn't seem to work right on complete ways. Possibly normal ways to?
            //{
            //    innerPolys = shapeList.Where(s => innerEntries.Contains(s.Id)).ToList();
            //    while (innerPolys.Count() > 0)
            //        innerPols.Add(GetShapeFromLines(ref innerPolys));
            //}

            //Remove any closed shapes first from the outer entries.
            //var closedShapes = shapeList.Where(s => s.Nodes.First().Id == s.Nodes.Last().Id).ToList();
            //foreach (var cs in closedShapes)
            //{
            //    if (cs.Nodes.Count() > 3) // if SimplifyAreas is true, this might have been a closedShape that became a linestring or point from this.
            //    {
            //        shapeList.Remove(cs);
            //        existingPols.Add(factory.CreatePolygon(cs.Nodes.Select(n => new Coordinate(n.Longitude.Value, n.Latitude.Value)).ToArray()));
            //    }
            //    else
            //        Log.WriteLog("Invalid closed shape found: " + cs.Id);
            //}

            //while (shapeList.Count() > 0)
            //    existingPols.Add(GetShapeFromLines(ref shapeList)); //only outers here.

            //existingPols = existingPols.Where(e => e != null).ToList();

            //if (existingPols.Count() == 0)
            //{
            //Log.WriteLog("Relation " + r.Id + " " + Place.GetPlaceName(r.Tags) + " has no polygons and no lines that make polygons. Is this relation supposed to be an open line?", Log.VerbosityLevels.High);
            //return null;
            //}

            outerPols = outerPols.Where(o => o != null).ToList();
            if (outerPols.Count == 0) //nothing was processed correctly.
                return null;

            if (outerPols.Count == 1) //innerPols.Count() >= 1)
            {
                //remove inner polygons
                var returnData = (Polygon)outerPols.First();
                foreach (var ir in innerPols) //this expects ways, not polygons/geometry.
                {
                    if (ir.Coordinates.First() == ir.Coordinates.Last())
                    {
                        var innerP = factory.CreateLineString(ir.Coordinates);
                        returnData.InteriorRings.Append(innerP);
                    }
                }
                return returnData;
            }

            //return a multipolygon instead.
            Geometry multiPol = factory.CreateMultiPolygon(outerPols.Distinct().ToArray());
            if (innerPols.Count() > 0)
            {
                var innerMultiPol = factory.CreateMultiPolygon(innerPols.Where(ip => ip != null).Distinct().ToArray());
                try
                {
                    multiPol = multiPol.Difference(innerMultiPol);
                }
                catch (Exception ex)
                {
                    Log.WriteLog("Relation " + r.Id + " Error trying to pull difference from inner and outer polygons:" + ex.Message);
                }
            }
            return multiPol;
        }

        public static Polygon GetShapeFromLines(ref List<WayData> shapeList)
        {
            //takes shapelist as ref, returns a polygon, leaves any other entries in shapelist to be called again.
            //NOTE: if this is a relation of lines that aren't a polygon (EX: a very long hiking trail), this should probably return the combined linestring? That's a different function

            List<Coordinate> possiblePolygon = new List<Coordinate>();
            var firstShape = shapeList.FirstOrDefault();
            if (firstShape == null)
            {
                Log.WriteLog("shapelist has 0 ways in shapelist?", Log.VerbosityLevels.High);
                return null;
            }
            shapeList.Remove(firstShape);
            var nextStartnode = firstShape.nds.Last();
            var processShape = false;
            var isError = false;
            possiblePolygon.AddRange(firstShape.nds.Where(n => n.id != nextStartnode.id).Select(n => new Coordinate(n.lon, n.lat)).ToList());
            while (processShape == false)
            {
                var allPossibleLines = shapeList.Where(s => s.nds.First().id == nextStartnode.id).ToList();
                if (allPossibleLines.Count > 1)
                {
                    Log.WriteLog("Shape has multiple possible lines to follow, might not process correctly.", Log.VerbosityLevels.High);
                }
                var lineToAdd = shapeList.Where(s => s.nds.First().id == nextStartnode.id && s.nds.First().id != s.nds.Last().id).FirstOrDefault();
                if (lineToAdd == null)
                {
                    //check other direction
                    var allPossibleLinesReverse = shapeList.Where(s => s.nds.Last().id == nextStartnode.id).ToList();
                    if (allPossibleLinesReverse.Count > 1)
                    {
                        Log.WriteLog("Way has multiple possible lines to follow, might not process correctly (Reversed Order).");
                    }
                    lineToAdd = shapeList.Where(s => s.nds.Last().id == nextStartnode.id && s.nds.First().id != s.nds.Last().id).FirstOrDefault();
                    if (lineToAdd == null)
                    {
                        //If all lines are joined and none remain, this might just be a relation of lines. Return a combined element
                        Log.WriteLog("shape doesn't seem to have properly connecting lines, can't process as polygon.", Log.VerbosityLevels.High);
                        processShape = true;
                        isError = true;
                    }
                    else
                        lineToAdd.nds.Reverse();
                }
                if (!isError)
                {
                    possiblePolygon.AddRange(lineToAdd.nds.Where(n => n.id != nextStartnode.id).Select(n => new Coordinate(n.lon, n.lat)).ToList());
                    nextStartnode = lineToAdd.nds.Last();
                    shapeList.Remove(lineToAdd);

                    if (possiblePolygon.First().Equals(possiblePolygon.Last()))
                        processShape = true;
                }
            }
            if (isError)
                return null;

            if (possiblePolygon.Count <= 3)
            {
                Log.WriteLog("Didn't find enough points to turn into a polygon. Probably an error.", Log.VerbosityLevels.High);
                return null;
            }

            var poly = factory.CreatePolygon(possiblePolygon.ToArray());
            poly = GeometrySupport.CCWCheck(poly);
            if (poly == null)
            {
                Log.WriteLog("Found a shape that isn't CCW either way. Error.", Log.VerbosityLevels.High);
                return null;
            }
            return poly;
        }

        public static Polygon GetShapeFromLines(ref List<OsmSharp.Complete.CompleteWay> shapeList)
        {
            //takes shapelist as ref, returns a polygon, leaves any other entries in shapelist to be called again.
            //NOTE: if this is a relation of lines that aren't a polygon (EX: a very long hiking trail), this should probably return the combined linestring? That's a different function
            //OR TODO: have this function check tags to determine if an entry is a polygon or a linestring that loops.

            //quick references.
            var startPoints = shapeList.Select(s => s.Nodes.First().Id).ToArray();
            var endPoints = shapeList.Select(s => s.Nodes.Last().Id).ToArray();

            List<Coordinate> possiblePolygon = new List<Coordinate>();
            var firstShape = shapeList.FirstOrDefault();
            if (firstShape == null)
            {
                Log.WriteLog("shapelist has 0 ways in shapelist?", Log.VerbosityLevels.High);
                return null;
            }
            shapeList.Remove(firstShape);
            var nextStartnode = firstShape.Nodes.Last();
            var processShape = false;
            var isError = false;
            possiblePolygon.AddRange(firstShape.Nodes.Where(n => n.Id != nextStartnode.Id).Select(n => new Coordinate(n.Longitude.Value, n.Latitude.Value)).ToList());
            while (processShape == false)
            {
                var allPossibleLines = shapeList.Where(s => s.Nodes.First().Id == nextStartnode.Id).ToList();
                if (allPossibleLines.Count > 1)
                {
                    Log.WriteLog("Shape has multiple possible lines to follow, might not process correctly.", Log.VerbosityLevels.High);
                }
                var lineToAdd = shapeList.Where(s => s.Nodes.First().Id == nextStartnode.Id && s.Nodes.First().Id != s.Nodes.Last().Id).FirstOrDefault();
                if (lineToAdd == null)
                {
                    //check other direction
                    var allPossibleLinesReverse = shapeList.Where(s => s.Nodes.Last().Id == nextStartnode.Id).ToList();
                    if (allPossibleLinesReverse.Count > 1)
                    {
                        Log.WriteLog("Way has multiple possible lines to follow, might not process correctly (Reversed Order).");
                    }
                    lineToAdd = shapeList.Where(s => s.Nodes.Last().Id == nextStartnode.Id && s.Nodes.First().Id != s.Nodes.Last().Id).FirstOrDefault();
                    if (lineToAdd == null)
                    {
                        //If all lines are joined and none remain, this might just be a relation of lines. Return a combined element
                        Log.WriteLog("shape doesn't seem to have properly connecting lines, can't process as polygon.", Log.VerbosityLevels.High);
                        processShape = true;
                        isError = true;
                    }
                    else
                        lineToAdd.Nodes = lineToAdd.Nodes.Reverse().ToArray();
                }
                if (!isError)
                {
                    possiblePolygon.AddRange(lineToAdd.Nodes.Where(n => n.Id != nextStartnode.Id).Select(n => new Coordinate(n.Longitude.Value, n.Latitude.Value)).ToList());
                    nextStartnode = lineToAdd.Nodes.Last();
                    shapeList.Remove(lineToAdd);

                    if (possiblePolygon.First().Equals(possiblePolygon.Last()))
                        processShape = true;
                }
            }
            if (isError)
                return null;

            if (possiblePolygon.Count <= 3)
            {
                Log.WriteLog("Didn't find enough points to turn into a polygon. Probably an error.", Log.VerbosityLevels.High);
                return null;
            }

            var poly = factory.CreatePolygon(possiblePolygon.ToArray());
            poly = GeometrySupport.CCWCheck(poly);
            if (poly == null)
            {
                Log.WriteLog("Found a shape that isn't CCW either way. Error.", Log.VerbosityLevels.High);
                return null;
            }
            return poly;
        }
    }
}
