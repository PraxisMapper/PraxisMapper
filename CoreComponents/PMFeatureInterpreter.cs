//Taken wholesale from OsmSharp and modified for PraxisMapper to fix bugs and optimize some logic.
//Specifically, the NoRecursion functions were done for Praxismapper to get around stack limits when
//parsing Ways of very large (11k+) nodes, and optimizing for PraxisMapper specifically.
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
using NetTopologySuite.Geometries;
using OsmSharp.Complete;
using OsmSharp.Tags;
using System.Collections.Generic;
using System.Linq;
using OsmSharp.Geo;
using OsmSharp;
using static PraxisCore.Singletons;

namespace PraxisCore
{
    /// <summary>
    /// PraxisMapper's modified feature interpreter. Doesn't use recursion, allowing for significantly larger relations and ways to be processed than the default OSMSharp interpreter.
    /// </summary>
    public class PMFeatureInterpreter
    {

        /// <summary>
        /// The list of tags that normally indicate an area unless specified otherwise.
        /// </summary>
        readonly static List<string> areaTags = new List<string>() { "building", "landuse", "amenity", "harbour", "historic", "leisure", "man_made", "military", "natural", "office",
                    "place", "public_transport", "shop", "sport", "tourism", "waterway", "wetland", "water", "aeroway"};

        /// <summary>
        /// Interprets an OSM-object and returns the corresponding geometry. Returns null on a failure.
        /// </summary>
        public Geometry Interpret(ICompleteOsmGeo osmObject)
        {
            // DISCLAIMER: this is a very very very simple geometry interpreter and
            // contains hardcoded all relevant tags.
            if (osmObject == null) return null;

            switch (osmObject.Type)
            {
                case OsmGeoType.Node:
                    //I have already filtered nodes down to only ones with tags that matter.
                    //No addditional processing on tags is needed here.
                    var n = osmObject as Node;
                    return new Point(new Coordinate(n.Longitude.Value, n.Latitude.Value));
                    //return new Point((osmObject as OsmSharp.Node).GetCoordinate());
                    break;
                case OsmGeoType.Way:
                    if (osmObject.Tags == null || osmObject.Tags.Count == 0)
                        return null;

                    bool isArea = false;
                    // check for a closed line if area.
                    //var coordinates = (osmObject as CompleteWay).GetCoordinates();
                    var coordinates = Converters.NodeArrayToCoordArray((osmObject as CompleteWay).Nodes);
                    if (coordinates.Length > 1 && coordinates[0].Equals2D(coordinates[coordinates.Length - 1]))
                    { // This might be an area, or just a line that ends where it starts. Look at tags to decide.
                        if (osmObject.Tags.ContainsAnyKey(areaTags)) //These tags normally default to an area regardless of the value provided.
                            isArea = true;
                        string areaVal = "";
                        if (osmObject.Tags.TryGetValue("area", out areaVal))
                        { // explicitly indicated that this is or isn't an area.
                            isArea = areaVal == "true";
                        }
                    }

                    if (isArea && coordinates.Length >= 4) // not a linearring, needs at least four coordinates, with first and last identical.
                    { // area tags leads to simple polygon
                        return new LinearRing(coordinates);
                    }
                    else if (coordinates.Length >= 2) // not a linestring, needs at least two coordinates.
                    { // no area tag leads to just a line.
                        return new LineString(coordinates);
                    }

                    break;
                case OsmGeoType.Relation:
                    if (osmObject.Tags == null || osmObject.Tags.Count == 0)
                        return null;

                    if (!osmObject.Tags.TryGetValue("type", out var typeValue))
                        return null;

                    var relation = (osmObject as CompleteRelation);

                    //{ // there is a type in this relation.
                    if (typeValue == "multipolygon" || typeValue == "linearring")
                    { // this relation is a multipolygon.
                        return InterpretMultipolygonRelationNoRecursion(relation);
                    }
                    else if (typeValue == "boundary" && relation.Tags.Contains("boundary", "administrative"))
                    { // this relation is a boundary.
                        return InterpretMultipolygonRelationNoRecursion(relation);
                    }
                    //}
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            return null;
        }

        /// <summary>
        /// Returns true if the given tags collection contains tags that could represents an area. waterway=river or waterway=stream could be potential false positives,
        /// but can't be confirmed without the actual geometry or the area=* tag set to true or false.
        /// </summary>
        public bool IsPotentiallyArea(TagsCollectionBase tags)
        {
            if (tags == null || tags.Count == 0)
                return false; // no tags, assume no area.

            if (tags.IsTrue("area"))
                return true;
            else if (tags.IsFalse("area"))
                return false;

            bool isArea = false;
            if (tags.ContainsAnyKey(areaTags))
                isArea = true;

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
            return isArea;
        }

        /// <summary>
        /// Creates a set of rings (as Polygons) from the provided CompleteWays. Closed Ways are used as-is, open Ways are joined together if possible.
        /// </summary>
        /// <param name="ways">a list of CompleteWays to turn into a Polygon</param>
        /// <returns>a list of Polygons made from the given Ways</returns>
        private static List<Polygon> BuildRings(List<CompleteWay> ways)
        {
            //This is where I look at points to try and combine these.
            var closedWays = new List<CompleteWay>(ways.Count);
            closedWays = ways.Where(w => w.Nodes.First().Id == w.Nodes.Last().Id).ToList();
            var polys = new List<Polygon>(ways.Count);

            foreach (var c in closedWays)
            {
                ways.Remove(c);
                polys.Add(factory.CreatePolygon(Converters.NodeArrayToCoordArray(c.Nodes)));
            }

            while (ways.Count > 0)
            {
                var a = GetShapeFromLines(ref ways);
                if (a != null)
                    polys.Add(a);
            }

            return polys;
        }

        /// <summary>
        /// Gives a list of Inner and Outer ways, creates a Geometry object representing the combined lists. Can return a Polygon or MultiPolygon depending on inputs.
        /// </summary>
        /// <param name="outerways">The list of outer ways. Must not be empty.</param>
        /// <param name="innerways">The list of inner ways. Maybe empty </param>
        /// <returns></returns>
        private Geometry BuildGeometry(List<CompleteWay> outerways, List<CompleteWay> innerways)
        {
            List<Polygon> outerRings;

            //Build outer rings first
            outerRings = BuildRings(outerways);
            if (outerRings == null || outerRings.Count == 0)
                return null;

            Geometry outer;
            if (outerRings.Count == 1)
                outer = outerRings.First();
            else
                outer = factory.CreateMultiPolygon(outerRings.ToArray());

            if (innerways.Count > 0)
            {
                List<Polygon> innerRings = new List<Polygon>(innerways.Count);
                innerRings = BuildRings(innerways);
                Geometry inner = factory.CreateMultiPolygon(innerRings.ToArray());
                outer = outer.Difference(inner);
            }

            return outer;
        }

        /// <summary>
        /// Parse a CompleteRelation into a Geometry. Avoids recursion to allow for much larger objects to be processed than the default OSMSharp FeatureInterpreter.
        /// </summary>
        /// <param name="relation">The CompleteRelation to process</param>
        /// <returns>the Geometry to use in the application elsewhere, or null if an error occurred generating the Geometry. </returns>
        private Geometry InterpretMultipolygonRelationNoRecursion(CompleteRelation relation)
        {
            //Feature feature = null;
            if (relation.Members == null)
            { // the relation has no members.
                return null;
            }

            // build lists of outer and inner ways.
            var inners = new List<CompleteWay>();
            var outers = new List<CompleteWay>(relation.Members.Length);

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
                    default:
                        break;
                }
            }

            var geometry = BuildGeometry(outers, inners);
            return geometry;
        }

        /// <summary>
        /// Takes the first way in a list, and searches for any/all ways that connect to it to form a closed polygon. Returns null if a closed shape cannot be formed.
        /// Call this repeatedly on a list until it is empty to find all polygons in a list of ways.
        /// </summary>
        /// <param name="shapeList">The list of ways to search. Will remove all ways that join to the first one or any joined to it from this list.</param>
        /// <returns>A Polygon if ways were able to be joined into a closed shape with the first way in the list, or null if not.</returns>
        private static Polygon GetShapeFromLines(ref List<CompleteWay> shapeList)
        {
            //NOTE/TODO: if this is a relation of lines that aren't a polygon (EX: a very long hiking trail), this should probably return the combined linestring?

            List<Node> currentShape = new List<Node>();
            var firstShape = shapeList.FirstOrDefault();
            if (firstShape == null)
            {
                Log.WriteLog("shapelist has 0 ways in shapelist?", Log.VerbosityLevels.Errors);
                return null;
            }

            Node originalStartPoint = firstShape.Nodes.First();

            shapeList.Remove(firstShape);
            var nextStartnode = firstShape.Nodes.Last();
            var closedShape = false;
            currentShape.AddRange(firstShape.Nodes);
            while (closedShape == false)
            {
                var lineToAdd = shapeList.FirstOrDefault(s => s.Nodes.First().Id == nextStartnode.Id);
                if (lineToAdd == null)
                {
                    //check other direction
                    lineToAdd = shapeList.FirstOrDefault(s => s.Nodes.Last().Id == nextStartnode.Id);
                    if (lineToAdd == null)
                    {
                        return null;
                    }
                    else
                        lineToAdd.Nodes = lineToAdd.Nodes.Reverse().ToArray(); //This way was drawn backwards relative to the original way.
                }
                currentShape.AddRange(lineToAdd.Nodes.Skip(1));
                nextStartnode = lineToAdd.Nodes.Last();
                shapeList.Remove(lineToAdd);

                if (nextStartnode.Id == originalStartPoint.Id)
                    closedShape = true;
                if (shapeList.Count == 0 && !closedShape)
                    return null;
            }

            if (currentShape.Count <= 3)
            {
                Log.WriteLog("Didn't find enough points to turn into a polygon. Probably an error in the source data.", Log.VerbosityLevels.Errors);
                return null;
            }

            var coordArray = Converters.NodeArrayToCoordArray(currentShape);
            var poly = factory.CreatePolygon(coordArray);
            //var poly = factory.CreatePolygon(currentShape.Select(s => new Coordinate((float)s.Longitude, (float)s.Latitude)).ToArray());
            poly = GeometrySupport.CCWCheck(poly);
            if (poly == null)
            {
                Log.WriteLog("Found a shape that isn't CCW either way. Error.", Log.VerbosityLevels.Errors);
                return null;
            }
            return poly;
        }
    }
}