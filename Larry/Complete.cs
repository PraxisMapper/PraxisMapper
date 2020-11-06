using CoreComponents;
using CoreComponents.Support;
using NetTopologySuite.Geometries;
using OsmSharp.Complete;
using System;
using System.Collections.Generic;
using System.Linq;
using static CoreComponents.DbTables;
using static CoreComponents.MapSupport;

namespace Larry
{
    public static class Complete
    {
        //This is for functions that operate on a CompleteGeo element, rather than one with references.

        public static MapData ProcessCompleteRelation(CompleteRelation r)
        {
            //This relation should contain all the ways it uses and their nodes, so I shouldn't need all my extra lookup stuff.
            PraxisContext db = new PraxisContext();

            string relationName = MapSupport.GetElementName(r.Tags);
            //Determine if we need to process this relation.
            //if all ways are closed outer polygons, we can skip this.
            //if all ways are lines that connect, we need to make it a polygon.
            //We can't always rely on tags being correct.

            //I might need to check if these are usable ways before checking if they're already handled by the relation
            //Remove entries we won't use.

            //var membersToRead = r.Members.Where(m => m.Type == OsmGeoType.Way).Select(m => m.Id).ToList();
            //if (membersToRead.Count == 0)
            //{
            //    Log.WriteLog("Relation " + r.Id + " " + relationName + " has no Ways, cannot process.");
            //    return null;
            //}

            //Check members for closed shape
            //var shapeList = new List<WayData>();
            //foreach (var m in membersToRead)
            //{
            //    var maybeWay = ways.Where(way => way.id == m).FirstOrDefault();
            //    if (maybeWay != null && maybeWay.nds.Count() > 3) //2-3 is a line, 1 is a point.
            //        shapeList.Add(maybeWay);
            //    else
            //    {
            //        Log.WriteLog("Relation " + r.Id + " " + relationName + " references way " + m + " not found in the file. Attempting to process without it.", Log.VerbosityLevels.High);
            //        //TODO: add some way of saving this partial data to the DB to be fixed/enhanced later.
            //        //break;
            //    }
            //}
            //membersToRead = null;

            //Now we have our list of Ways. Check if there's lines that need made into a polygon.
            //if (shapeList.Any(s => s.nds.Count == 0))
            //{
            //    Log.WriteLog("Relation " + r.Id + " " + relationName + " has ways with 0 nodes.");
            //}

            Geometry Tpoly = GetGeometryFromMembers(r.Members);
            if (Tpoly == null)
            {
                //error converting it
                Log.WriteLog("Relation " + r.Id + " " + relationName + " failed to get a polygon from ways. Error.");
                return null;
            }

            if (!Tpoly.IsValid)
            {
                //System.Diagnostics.Debugger.Break();
                Log.WriteLog("Relation " + r.Id + " " + relationName + " Is not valid geometry. Error.");
                return null;
            }

            MapData md = new MapData();
            md.name = MapSupport.GetElementName(r.Tags);
            md.type = MapSupport.GetType(r.Tags);
            md.AreaTypeId = MapSupport.areaTypeReference[md.type.StartsWith("admin") ? "admin" : md.type].First();
            md.RelationId = r.Id;
            md.place = MapSupport.SimplifyArea(Tpoly);
            if (md.place == null)
                return null;

            return md;
        }

        private static Geometry GetGeometryFromMembers(CompleteRelationMember[] members)
        {
            //A common-ish case looks like the outer entries are lines that join togetehr, and inner entries are polygons.
            //Let's see if we can build a polygon (or more, possibly)
            List<Coordinate> possiblePolygon = new List<Coordinate>();
            //from the first line, find the line that starts with the same endpoint (or ends with the startpoint, but reverse that path).
            //continue until a line ends with the first node. That's a closed shape.

            List<Polygon> existingPols = new List<Polygon>();
            List<Polygon> innerPols = new List<Polygon>();

            //Somewhat uncommon issue: A relation might create multiple closed polygons from lines.
            //OR contains both closed polygons and ways that create closed polygons
            //so lets start looking for those too.

            //if (shapeList.Count == 0)
            //{
            //Log.WriteLog("Relation " + r.Id + " " + GetElementName(r.Tags) + " has 0 ways in shapelist", Log.VerbosityLevels.High);
            //return null;
            //}

            //Separate sets
            var innerEntries = members.Where(m => m.Role == "inner").Select(m => (CompleteWay)m.Member).ToList(); //these are almost always closed polygons.
            var outerEntries = members.Where(m => m.Role == "outer").Select(m => (CompleteWay)m.Member).ToList();
            var innerPolys = new List<WayData>();

            //if (innerEntries.Count() + outerEntries.Count() > shapeList.Count)
            //{
            //    Log.WriteLog("Relation " + r.Id + " " + GetElementName(r.Tags) + " is missing Ways, odds of success are low.", Log.VerbosityLevels.High);
            //}

            //Not all ways are tagged for this, so we can't always rely on this.
            //if (outerEntries.Count > 0)
            //shapeList = shapeList.Where(s => outerEntries.Contains(s.id)).ToList();

            //if (innerEntries.Count > 0)
            {
                //innerPolys = shapeList.Where(s => innerEntries.Contains(s.id)).ToList();
                //foreach (var ie in innerPolys)
                //{
                //while (innerPolys.Count() > 0)
                //TODO: confirm all of these are closed shapes.
                //innerPols.Add(GetShapeFromLines(ref innerPolys));
                //}
                //}

                //Remove any closed shapes first from the outer entries.
                var closedShapes = outerEntries.Where(s => s.Nodes.First().Id == s.Nodes.Last().Id).ToList();
                foreach (var cs in closedShapes)
                {
                    outerEntries.Remove(cs);
                    existingPols.Add(factory.CreatePolygon(cs.Nodes.Select(n => new Coordinate(n.Longitude.Value, n.Latitude.Value)).ToArray()));
                }

                while (outerEntries.Count() > 0)
                    existingPols.Add(GetShapeFromCompleteWay(ref outerEntries)); //only outers here.

                existingPols = existingPols.Where(e => e != null).ToList();

                if (existingPols.Count() == 0)
                {
                    Log.WriteLog("This relation has no polygons and no lines that make polygons. Is this relation supposed to be an open line?", Log.VerbosityLevels.High);
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
                            var innerP = factory.CreateLineString(WayToCoordArray(ir));
                            returnData.InteriorRings.Append(innerP);
                        }
                    }
                    return returnData;
                }

                //return a multipolygon instead.
                Geometry multiPol = factory.CreateMultiPolygon(existingPols.Distinct().ToArray());
                //A new attempt at removing inner entries from outer ones via multipolygon.
                if (innerPols.Count() > 0)
                {
                    var innerMultiPol = factory.CreateMultiPolygon(innerPols.Where(ip => ip != null).Distinct().ToArray());
                    try
                    {
                        multiPol = multiPol.Difference(innerMultiPol);
                    }
                    catch (Exception ex)
                    {
                        Log.WriteLog("Error trying to pull difference from inner and outer polygons:" + ex.Message);
                    }
                }
                return multiPol;
            }
        }

        private static Polygon GetShapeFromCompleteWay(ref List<CompleteWay> shapeList)
        {
            //takes shapelist as ref, returns a polygon, leaves any other entries in shapelist to be called again.
            //NOTE/TODO: if this is a relation of lines that aren't a polygon (EX: a very long hiking trail), this should probably return the combined linestring?
            //TODO: if the lines are too small, should I return a Point instead?

            List<Coordinate> possiblePolygon = new List<Coordinate>();
            var firstShape = shapeList.FirstOrDefault();
            if (firstShape == null)
            {
                Log.WriteLog("shapelist has 0 ways in shapelist?", Log.VerbosityLevels.High);
                return null;
            }
            shapeList.Remove(firstShape);
            var nextStartnode = firstShape.Nodes.Last();
            var closedShape = false;
            var isError = false;
            possiblePolygon.AddRange(firstShape.Nodes.Where(n => n.Id != nextStartnode.Id).Select(n => new Coordinate(n.Longitude.Value, n.Latitude.Value)).ToList());
            while (closedShape == false)
            {
                var allPossibleLines = shapeList.Where(s => s.Nodes.First().Id == nextStartnode.Id).ToList();
                if (allPossibleLines.Count() > 1)
                {
                    Log.WriteLog("Shape has multiple possible lines to follow, might not process correctly.", Log.VerbosityLevels.High);
                }
                var lineToAdd = shapeList.Where(s => s.Nodes.First().Id == nextStartnode.Id && s.Nodes.First().Id != s.Nodes.Last().Id).FirstOrDefault();
                if (lineToAdd == null)
                {
                    //check other direction
                    var allPossibleLinesReverse = shapeList.Where(s => s.Nodes.Last().Id == nextStartnode.Id).ToList();
                    if (allPossibleLinesReverse.Count() > 1)
                    {
                        Log.WriteLog("Way has multiple possible lines to follow, might not process correctly (Reversed Order).");
                    }
                    lineToAdd = shapeList.Where(s => s.Nodes.Last().Id == nextStartnode.Id && s.Nodes.First().Id != s.Nodes.Last().Id).FirstOrDefault();
                    if (lineToAdd == null)
                    {
                        //If all lines are joined and none remain, this might just be a relation of lines. Return a combined element
                        Log.WriteLog("shape doesn't seem to have properly connecting lines, can't process as polygon.", Log.VerbosityLevels.High);
                        closedShape = true; //rename this to something better for breaking the loop
                        isError = true; //rename this to something like IsPolygon
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
                        closedShape = true;
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
            poly = CCWCheck(poly);
            if (poly == null)
            {
                Log.WriteLog("Found a shape that isn't CCW either way. Error.", Log.VerbosityLevels.High);
                return null;
            }
            return poly;
        }
    }
}
