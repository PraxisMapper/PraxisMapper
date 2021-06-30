//Taken wholesale from OsmSharp and modified for PraxisMapper to fix bugs while waiting for updates.
//This file remains distributed under the original license:
// The MIT License (MIT)

// Copyright (c) 2016 Ben Abelshausen

// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:

// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.

// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using System;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using OsmSharp.Complete;
using OsmSharp.Logging;
using OsmSharp.Tags;
using System.Collections.Generic;
using System.Linq;
using OsmSharp.Geo;
using OsmSharp;
using ProtoBuf.Serializers;
using static CoreComponents.Singletons;

namespace CoreComponents
{
    /// <summary>
    /// The default feature interpreter.
    /// </summary>
    public class PMFeatureInterpreter : FeatureInterpreter
    {
        /// <summary>
        /// Interprets an OSM-object and returns the corresponding geometry.
        /// </summary>
        public override FeatureCollection Interpret(ICompleteOsmGeo osmObject)
        {
            // DISCLAIMER: this is a very very very simple geometry interpreter and
            // contains hardcoded all relevant tags.

            var collection = new FeatureCollection();
            if (osmObject == null) return collection;

            TagsCollectionBase tags;
            switch (osmObject.Type)
            {
                case OsmGeoType.Node:
                    var newCollection = new TagsCollection(
                        osmObject.Tags);
                    newCollection.RemoveKey("FIXME");
                    newCollection.RemoveKey("node");
                    newCollection.RemoveKey("source");

                    if (newCollection.Count > 0)
                    { // there is still some relevant information left.
                        collection.Add(new Feature(new Point((osmObject as OsmSharp.Node).GetCoordinate()),
                            TagsAndIdToAttributes(osmObject)));
                    }
                    break;
                case OsmGeoType.Way:
                    tags = osmObject.Tags;

                    if (tags == null)
                    {
                        return collection;
                    }

                    bool isArea = false;
                    if ((tags.ContainsKey("building") && !tags.IsFalse("building")) ||
                        (tags.ContainsKey("landuse") && !tags.IsFalse("landuse")) ||
                        (tags.ContainsKey("amenity") && !tags.IsFalse("amenity")) ||
                        (tags.ContainsKey("harbour") && !tags.IsFalse("harbour")) ||
                        (tags.ContainsKey("historic") && !tags.IsFalse("historic")) ||
                        (tags.ContainsKey("leisure") && !tags.IsFalse("leisure")) ||
                        (tags.ContainsKey("man_made") && !tags.IsFalse("man_made")) ||
                        (tags.ContainsKey("military") && !tags.IsFalse("military")) ||
                        (tags.ContainsKey("natural") && !tags.IsFalse("natural")) ||
                        (tags.ContainsKey("office") && !tags.IsFalse("office")) ||
                        (tags.ContainsKey("place") && !tags.IsFalse("place")) ||
                        (tags.ContainsKey("public_transport") && !tags.IsFalse("public_transport")) ||
                        (tags.ContainsKey("shop") && !tags.IsFalse("shop")) ||
                        (tags.ContainsKey("sport") && !tags.IsFalse("sport")) ||
                        (tags.ContainsKey("tourism") && !tags.IsFalse("tourism")) ||
                        (tags.ContainsKey("waterway") && !tags.IsFalse("waterway")) ||
                        (tags.ContainsKey("wetland") && !tags.IsFalse("wetland")) ||
                        (tags.ContainsKey("water") && !tags.IsFalse("water")) ||
                        (tags.ContainsKey("aeroway") && !tags.IsFalse("aeroway")))
                    { // these tags usually indicate an area.
                        isArea = true;
                    }

                    if (tags.IsTrue("area"))
                    { // explicitly indicated that this is an area.
                        isArea = true;
                    }
                    else if (tags.IsFalse("area"))
                    { // explicitly indicated that this is not an area.
                        isArea = false;
                    }

                    // check for a closed line if area.
                    var coordinates = (osmObject as CompleteWay).GetCoordinates();
                    if (isArea && coordinates.Count > 1 &&
                        !coordinates[0].Equals2D(coordinates[coordinates.Count - 1]))
                    { // not an area, first and last coordinate do not match.
                        Logger.Log("DefaultFeatureInterpreter", TraceEventType.Warning, "{0} is supposed to be an area but first and last coordinates do not match.",
                            osmObject.ToInvariantString());
                    }
                    else if (!isArea && coordinates.Count < 2)
                    { // not a linestring, needs at least two coordinates.
                        Logger.Log("DefaultFeatureInterpreter", TraceEventType.Warning, "{0} is supposed to be a linestring but has less than two coordinates.",
                            osmObject.ToInvariantString());
                    }
                    else if (isArea && coordinates.Count < 4)
                    {// not a linearring, needs at least four coordinates, with first and last identical.
                        Logger.Log("DefaultFeatureInterpreter", TraceEventType.Warning, "{0} is supposed to be a linearring but has less than four coordinates.",
                            osmObject.ToInvariantString());
                    }
                    else
                    {
                        if (isArea)
                        { // area tags leads to simple polygon
                            var lineairRing = new Feature(new LinearRing(coordinates.
                                ToArray<Coordinate>()), TagsAndIdToAttributes(osmObject));
                            collection.Add(lineairRing);
                        }
                        else
                        { // no area tag leads to just a line.
                            var lineString = new Feature(new LineString(coordinates.
                                ToArray<Coordinate>()), TagsAndIdToAttributes(osmObject));
                            collection.Add(lineString);
                        }
                    }
                    break;
                case OsmGeoType.Relation:
                    var relation = (osmObject as CompleteRelation);
                    tags = relation.Tags;

                    if (tags == null)
                    {
                        return collection;
                    }

                    if (tags.TryGetValue("type", out var typeValue))
                    { // there is a type in this relation.
                        if (typeValue == "multipolygon" || typeValue == "linearring")
                        { // this relation is a multipolygon.
                            var feature = this.InterpretMultipolygonRelation(relation);
                            if (feature != null)
                            { // add the geometry.
                                collection.Add(feature);
                            }
                        }
                        else if (typeValue == "boundary" && tags.Contains("boundary", "administrative"))
                        { // this relation is a boundary.
                            var feature = this.InterpretMultipolygonRelation(relation);
                            if (feature != null)
                            { // add the geometry.
                                collection.Add(feature);
                            }
                        }
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            return collection;
        }

        /// <summary>
        /// Returns true if the given tags collection contains tags that could represents an area.
        /// </summary>
        public override bool IsPotentiallyArea(TagsCollectionBase tags)
        {
            if (tags == null || tags.Count == 0) { return false; } // no tags, assume no area.

            bool isArea = false;
            if ((tags.ContainsKey("building") && !tags.IsFalse("building")) ||
                (tags.ContainsKey("landuse") && !tags.IsFalse("landuse")) ||
                (tags.ContainsKey("amenity") && !tags.IsFalse("amenity")) ||
                (tags.ContainsKey("harbour") && !tags.IsFalse("harbour")) ||
                (tags.ContainsKey("historic") && !tags.IsFalse("historic")) ||
                (tags.ContainsKey("leisure") && !tags.IsFalse("leisure")) ||
                (tags.ContainsKey("man_made") && !tags.IsFalse("man_made")) ||
                (tags.ContainsKey("military") && !tags.IsFalse("military")) ||
                (tags.ContainsKey("natural") && !tags.IsFalse("natural")) ||
                (tags.ContainsKey("office") && !tags.IsFalse("office")) ||
                (tags.ContainsKey("place") && !tags.IsFalse("place")) ||
                (tags.ContainsKey("power") && !tags.IsFalse("power")) ||
                (tags.ContainsKey("public_transport") && !tags.IsFalse("public_transport")) ||
                (tags.ContainsKey("shop") && !tags.IsFalse("shop")) ||
                (tags.ContainsKey("sport") && !tags.IsFalse("sport")) ||
                (tags.ContainsKey("tourism") && !tags.IsFalse("tourism")) ||
                (tags.ContainsKey("waterway") && !tags.IsFalse("waterway") && !tags.Contains("waterway", "river") && !tags.Contains("waterway", "stream")) ||
                (tags.ContainsKey("wetland") && !tags.IsFalse("wetland")) ||
                (tags.ContainsKey("water") && !tags.IsFalse("water")) ||
                (tags.ContainsKey("aeroway") && !tags.IsFalse("aeroway")))
            {
                isArea = true;
            }

            if (tags.TryGetValue("type", out var typeValue))
            {
                switch (typeValue)
                {
                    // there is a type in this relation.
                    case "multipolygon":
                    // this relation is a boundary.
                    case "boundary": // this relation is a multipolygon.
                        isArea = true;
                        break;
                }
            }

            if (tags.IsTrue("area"))
            { // explicitly indicated that this is an area.
                isArea = true;
            }
            else if (tags.IsFalse("area"))
            { // explicitly indicated that this is not an area.
                isArea = false;
            }

            return isArea;
        }

        /// <summary>
        /// Tries to interpret a given multipolygon relation.
        /// </summary>
        private Feature InterpretMultipolygonRelation(CompleteRelation relation)
        {
            Feature feature = null;
            if (relation.Members == null)
            { // the relation has no members.
                return null;
            }

            // build lists of outer and inner ways.
            var ways = new List<KeyValuePair<bool, CompleteWay>>(); // outer=true
            foreach (var member in relation.Members)
            {
                switch (member.Role)
                {
                    case "inner" when member.Member is CompleteWay: // an inner member.
                        ways.Add(new KeyValuePair<bool, CompleteWay>(
                            false, member.Member as CompleteWay));
                        break;
                    case "outer" when member.Member is CompleteWay: // an outer member.
                        ways.Add(new KeyValuePair<bool, CompleteWay>(
                            true, member.Member as CompleteWay));
                        break;
                }
            }

            // started a similar algorithm and then found this:
            // loosely based on: http://wiki.openstreetmap.org/wiki/Relation:multipolygon/Algorithm

            // recusively try to assign the rings.
            //Console.WriteLine("starting ring assignment for relation " + relation.Id + "(" + ways.Count + (" ways)"));
            if (!this.AssignRings(ways, out var rings))
            {
                Log.WriteLog($"Ring assignment failed: invalid multipolygon relation [{relation.Id}] detected!");
                //Logging.Logger.Log("DefaultFeatureInterpreter", TraceEventType.Error,
                //$"Ring assignment failed: invalid multipolygon relation [{relation.Id}] detected!");
            }
            // group the rings and create a multipolygon.
            var geometry = this.GroupRings(rings);
            if (geometry != null)
            {
                feature = new Feature(geometry, TagsAndIdToAttributes(relation));
            }
            return feature;
        }

        /// <summary>
        /// Groups the rings into polygons.
        /// </summary>
        private Geometry GroupRings(List<KeyValuePair<bool, LinearRing>> rings)
        {
            Geometry geometry = null;
            var containsFlags = new bool[rings.Count][]; // means [x] contains [y]
            for (var x = 0; x < rings.Count; x++)
            {
                var xPolygon = new Polygon(rings[x].Value);
                containsFlags[x] = new bool[rings.Count];
                for (var y = 0; y < rings.Count; y++)
                {
                    var yPolygon = new Polygon(rings[y].Value);
                    try
                    {
                        containsFlags[x][y] = xPolygon.Contains(yPolygon);
                    }
                    catch (TopologyException)
                    {
                        return null;
                    }
                }
            }
            var used = new bool[rings.Count];
            List<Polygon> multiPolygon = null;
            while (used.Contains(false))
            { // select a ring not contained by any other.
                LinearRing outer = null;
                int outerIdx = -1;
                for (int idx = 0; idx < rings.Count; idx++)
                {
                    if (!used[idx] && this.CheckUncontained(rings, containsFlags, used, idx))
                    { // this ring is not contained in any other used rings.
                        if (!rings[idx].Key)
                        {
                            Log.WriteLog("Invalid multipolygon relation: an 'inner' ring was detected without an 'outer'.");
                            //Logging.Logger.Log("DefaultFeatureInterpreter", TraceEventType.Error,
                            //"Invalid multipolygon relation: an 'inner' ring was detected without an 'outer'.");
                        }
                        outerIdx = idx;
                        outer = rings[idx].Value;
                        used[idx] = true;
                        break;
                    }
                }
                if (outer != null)
                { // an outer ring was found, find inner rings.
                    var inners = new List<LinearRing>();
                    // select all rings contained by inner but not by any others.
                    for (int idx = 0; idx < rings.Count; idx++)
                    {
                        if (!used[idx] && containsFlags[outerIdx][idx] &&
                            this.CheckUncontained(rings, containsFlags, used, idx))
                        {
                            inners.Add(rings[idx].Value);
                            used[idx] = true;
                        }
                    }

                    var unused = !used.Contains(false);
                    if (multiPolygon == null &&
                        inners.Count == 0 &&
                        unused)
                    { // there is just one lineair ring.
                        geometry = outer;
                        break;
                    }
                    else if (multiPolygon == null &&
                        unused)
                    { // there is just one polygon.
                        var polygon = new Polygon(
                            outer, inners.ToArray());
                        geometry = polygon;
                        break;
                    }
                    else
                    { // there have to be other polygons.
                        if (multiPolygon == null)
                        {
                            multiPolygon = new List<Polygon>();
                        }
                        multiPolygon.Add(new Polygon(outer, inners.ToArray()));
                        geometry = new MultiPolygon(multiPolygon.ToArray());
                    }
                }
                else
                { // unused rings left but they cannot be designated as 'outer'.
                    //Log.WriteLog("Invalid multipolygon relation: Unassigned rings left.");
                    //Logging.Logger.Log("DefaultFeatureInterpreter", TraceEventType.Error,
                    //    "Invalid multipolygon relation: Unassigned rings left.");
                    break;
                }
            }
            return geometry;
        }

        /// <summary>
        /// Checks if a ring is not contained by any other unused ring.
        /// </summary>
        private bool CheckUncontained(List<KeyValuePair<bool, LinearRing>> rings,
            bool[][] containsFlags, bool[] used, int ringIdx)
        {
            for (var i = 0; i < rings.Count; i++)
            {
                if (i != ringIdx && !used[i] && containsFlags[i][ringIdx])
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Tries to extract all rings from the given ways.
        /// </summary>
        private bool AssignRings(List<KeyValuePair<bool, CompleteWay>> ways, out List<KeyValuePair<bool, LinearRing>> rings)
        {
            return this.AssignRings(ways, new bool[ways.Count], out rings);
        }

        /// <summary>
        /// Assigns rings to the unassigned ways.
        /// </summary>
        private bool AssignRings(List<KeyValuePair<bool, CompleteWay>> ways, bool[] assignedFlags, out List<KeyValuePair<bool, LinearRing>> rings)
        {
            //rings = new List<KeyValuePair<bool, LinearRing>>();
            var assigned = false;
            for (var i = 0; i < ways.Count; i++)
            {
                if (!assignedFlags[i])
                {
                    assigned = true;
                    LinearRing ring;
                    if (this.AssignRing(ways, i, assignedFlags, out ring))
                    { // assigning the ring successed.
                        List<KeyValuePair<bool, LinearRing>> otherRings;
                        if (this.AssignRings(ways, assignedFlags, out otherRings)) //This recursive stack eventually fails on the Labrador Sea, 25,000 ways.
                        { // assigning the rings succeeded.
                            rings = otherRings;
                            rings.Add(new KeyValuePair<bool, LinearRing>(ways[i].Key, ring));
                            return true;
                        }
                    }
                }
            }
            rings = new List<KeyValuePair<bool, LinearRing>>();
            return !assigned;
        }

        private static List<Polygon> BuildRings(List<CompleteWay> ways)
        {
            //This is where I look at points to try and combine these.
            var closedWays = new List<CompleteWay>();
            closedWays = ways.Where(w => w.Nodes.First() == w.Nodes.Last()).ToList();
            var polys = new List<Polygon>();

            foreach (var c in closedWays)
            {
                ways.Remove(c);
                polys.Add(factory.CreatePolygon(Converters.CompleteWayToCoordArray(c)));
            }
            
            while (ways.Count() > 0)
            {
                var a = GetShapeFromLines(ref ways);
                if (a != null)
                    polys.Add(a);
            }

            return polys;
        }

        //This should return a list of rings? Or the completed geometry?
        private Geometry BuildGeometry(List<CompleteWay> outerways, List<CompleteWay> innerways)
        {
            var outerRings = new List<Polygon>();
            var innerRings = new List<Polygon>();

            //Build outer rings first
            outerRings = BuildRings(outerways);
            if (outerRings == null || outerRings.Count == 0)
                return null;
           
            innerRings = BuildRings(innerways);

            Geometry outer = null;
            Geometry inner = null;
            if (outerRings.Count() == 1)
                outer = outerRings.First();
            else
                outer = factory.CreateMultiPolygon(outerRings.ToArray());

            if (innerRings.Count() > 0)
            {
                inner = factory.CreateMultiPolygon(innerRings.ToArray());
                outer = outer.Difference(inner);
            }

            return outer;
        }

        /// <summary>
        /// Creates a new lineair ring from the given way and updates the assigned flags array.
        /// </summary>
        private bool AssignRing(List<KeyValuePair<bool, CompleteWay>> ways, int way, bool[] assignedFlags, out LinearRing ring)
        {
            List<Coordinate> coordinates = null;
            assignedFlags[way] = true;
            if (ways[way].Value.IsClosed())
            { // the was is closed.
                coordinates = ways[way].Value.GetCoordinates();
            }
            else
            { // the way is open.
                var roleFlag = ways[way].Key;

                // complete the ring.
                var nodes = new List<Node>(ways[way].Value.Nodes);
                if (this.CompleteRing(ways, assignedFlags, nodes, roleFlag))
                { // the ring was completed!
                    coordinates = new List<Coordinate>(nodes.Count);
                    foreach (var node in nodes)
                    {
                        coordinates.Add(node.GetCoordinate());
                    }
                }
                else
                { // oops, assignment failed: backtrack again!
                    assignedFlags[way] = false;
                    ring = null;
                    return false;
                }
            }
            ring = new LinearRing(coordinates.ToArray());
            return true;
        }

        /// <summary>
        /// Completes an uncompleted ring.
        /// </summary>
        /// <param name="ways"></param>
        /// <param name="assignedFlags"></param>
        /// <param name="nodes"></param>
        /// <param name="role"></param>
        /// <returns></returns>
        private bool CompleteRing(List<KeyValuePair<bool, CompleteWay>> ways, bool[] assignedFlags,
            List<Node> nodes, bool? role)
        {
            //This is also a recursive function, and might actually be the one hitting the stack overflow?
            for (var idx = 0; idx < ways.Count; idx++)
            {
                if (!assignedFlags[idx])
                { // way not assigned.
                    var wayEntry = ways[idx];
                    var nextWay = wayEntry.Value;
                    if (!role.HasValue || wayEntry.Key == role.Value)
                    { // only try matching roles if the role has been set.
                        List<Node> nextNodes = null;
                        if (nodes[nodes.Count - 1].Id == nextWay.Nodes[0].Id)
                        { // last node of the previous way is the first node of the next way.
                            nextNodes = nextWay.Nodes.GetRange(1, nextWay.Nodes.Length - 1);
                            assignedFlags[idx] = true;
                        }
                        else if (nodes[nodes.Count - 1].Id == nextWay.Nodes[nextWay.Nodes.Length - 1].Id)
                        { // last node of the previous way is the last node of the next way.
                            nextNodes = nextWay.Nodes.GetRange(0, nextWay.Nodes.Length - 1);
                            nextNodes.Reverse();
                            assignedFlags[idx] = true;
                        }

                        // add the next nodes if any.
                        if (assignedFlags[idx])
                        { // yep, way was assigned!
                            nodes.AddRange(nextNodes);
                            if (nodes[nodes.Count - 1].Id == nodes[0].Id)
                            { // yes! a closed ring was found!
                                return true;
                            }
                            else
                            { // noo! ring not closed yet!
                                if (this.CompleteRing(ways.Where(w => !w.Key).ToList(), assignedFlags, nodes, role))
                                { // yes! a complete ring was found
                                    return true;
                                }
                                else
                                { // damn complete ring not found. backtrack people!
                                    assignedFlags[idx] = false;
                                    nodes.RemoveRange(nodes.Count - nextNodes.Count, nextNodes.Count);
                                }
                            }
                        }
                    }
                }
            }
            return false;
        }

        private static AttributesTable TagsAndIdToAttributes(ICompleteOsmGeo osmObject)
        {
            var attr = osmObject.Tags.ToAttributeTable();
            attr.Add("id", osmObject.Id);

            return attr;
        }

        //These functions are new and PraxisMapper specific, but currently use older classes that need updated to OsmSharp ones.
        //Meant to get results without being recursive, to avoid stack overflows the way that AssignRings
        //above can on very large ways.

        private Feature InterpretMultipolygonRelationNoRecursion(CompleteRelation relation)
        {
            Feature feature = null;
            if (relation.Members == null)
            { // the relation has no members.
                return null;
            }

            // build lists of outer and inner ways.
            var inners = new List<CompleteWay>();
            var outers = new List<CompleteWay>();

            foreach (var member in relation.Members)
            {
                switch (member.Role)
                {
                    case "inner" when member.Member is CompleteWay:
                        inners.Add(member.Member as CompleteWay);
                        break;
                    case "outer" when member.Member is CompleteWay:
                        outers.Add(member.Member as CompleteWay);
                        break;
                }
            }

            var geometry = BuildGeometry(outers, inners);

            if (geometry != null)
            {
                feature = new Feature(geometry, TagsAndIdToAttributes(relation));
            }
            return feature;

        }
        
        private static Polygon GetShapeFromLines(ref List<CompleteWay> shapeList)
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
            possiblePolygon.AddRange(firstShape.Nodes.Where(n => n.Id != nextStartnode.Id).Select(n => new Coordinate((float)n.Longitude, (float)n.Latitude)).ToList());
            while (closedShape == false)
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
                        closedShape = true; //rename this to something better for breaking the loop
                        isError = true; //rename this to something like IsPolygon
                    }
                    else
                        lineToAdd.Nodes.Reverse();
                }
                if (!isError)
                {
                    possiblePolygon.AddRange(lineToAdd.Nodes.Where(n => n.Id != nextStartnode.Id).Select(n => new Coordinate((float)n.Longitude, (float)n.Latitude)).ToList());
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
