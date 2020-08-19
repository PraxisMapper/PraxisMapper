using OsmXmlParser.Classes;
using System;
using System.Collections.Generic;
using System.ComponentModel.Design.Serialization;
using System.IO.Enumeration;
using System.Linq;
using System.Net;
using System.Xml;

namespace OsmXmlParser
{
    class Program
    {
        //
        public static List<Bounds> bounds = new List<Bounds>();
        public static List<Node> nodes = new List<Node>();
        public static List<Way> ways = new List<Way>();
        public static List<Relation> relations = new List<Relation>();

        public static Lookup<long, Node> nodeLookup;
        public static Lookup<long, Way> wayLookup;

        static void Main(string[] args)
        {
            //Console.WriteLine("Hello World!");

            //Hardcoding test logic here.
            //TODO: read xml from zip file. Pretty sure I can stream zipfile data to save HD space, since global XML data needs 1TB of space unzipped.
            string filename = @"..\..\..\jamaica-latest.osm"; //this one takes about 11 seconds to run though, 500MB
            //string filename = @"C:\Users\Drake\Downloads\us-midwest-latest.osm\us-midwest-latest.osm"; //This one takes much longer to process, 28 GB
            XmlReaderSettings xrs = new XmlReaderSettings();
            xrs.IgnoreWhitespace = true;
            XmlReader osmFile = XmlReader.Create(filename, xrs);
            osmFile.MoveToContent();

            while (osmFile.Read())
            {
                //This takes about 11 seconds right now to parse Jamaica's full file with XmlReader
                //Its fast enough where I may not bother to remove unused data from the source xml.
                switch (osmFile.Name)
                {
                    case "bounds": //the logical limits of the data in this file.
                        var b = new Bounds();
                        b.minlon = osmFile.GetAttribute("minlon").ToDouble();
                        b.minlat = osmFile.GetAttribute("minlat").ToDouble();
                        b.maxlon = osmFile.GetAttribute("maxlon").ToDouble();
                        b.maxlat = osmFile.GetAttribute("maxlat").ToDouble();
                        bounds.Add(b);
                        break;
                    case "node":
                        //It's a shame we can't dig farther into the XML file to know if this node is needed or not until we've loaded them all.
                        var n = new Node();
                        n.id = osmFile.GetAttribute("id").ToLong();
                        n.lat = osmFile.GetAttribute("lat").ToDouble();
                        n.lon = osmFile.GetAttribute("lon").ToDouble();
                        //nodes might have tags with useful info
                        //if (osmFile.NodeType == XmlNodeType.Element)
                        n.tags = parseTags(osmFile.ReadSubtree());
                        //TODO: delete note tags that aren't interesting //EX: created-by, 
                        //TODO: delete nodes that only belonged to boring ways/relations.
                        nodes.Add(n);
                        break;
                    case "way":
                        //One-shot update this for speed
                        if (nodeLookup == null)
                            nodeLookup = (Lookup<long, Node>)nodes.ToLookup(k => k.id, v => v);
                        //way will contain 'nd' elements with 'ref' to a node by ID.
                        //may contain tag, k is the type of thing it is.
                        var w = new Way();
                        w.id = osmFile.GetAttribute("id").ToLong();

                        ParseWayData(w, osmFile.ReadSubtree());
                        //w.nds = parseWayNodes(osmFile.ReadSubtree());
                        //w.tags = parseTags(osmFile.ReadSubtree());

                        //TODO: delete ways that are just buildings with no additional markers.
                        if (!w.tags.Any(t => t.k == "building"  //don't add buildings
                                || t.k == "runway" //we dont need runways.
                                || t.k == "highway" //ignoring roads.
                                )) 
                            ways.Add(w);
                        break;
                    case "relation":
                        //again, one shot update
                        if (wayLookup == null)
                            wayLookup = (Lookup<long, Way>)ways.ToLookup(k => k.id, v => v);
                        //relation has 'member' with a type (usually to a 'way' entry) and a ref to their id
                        //Can also have tags with various keys (k). Which ones are intersting?
                        //see https://wiki.openstreetmap.org/wiki/Map_Features for what's what here.
                        //k = "natural", "tourism", "power", "sport", "leisure", "landuse"[residential, cemetary], "Waterway", type=route, route=foot ?
                        //Some of these will be complicated because people dont agree on which to use.
                        //EX: leisure/park is technically a park, but could also be boundary/national_park or sometimes landuse/forest etc.
                        //My local park is leisure/park
                        //type=restriction is one we can ignore. I dont care about no U turn signs or whatnot.
                        //Turning these boxes into PlusCodes is a later task.

                        Relation r = new Relation();
                        r.id = osmFile.GetAttribute("id").ToLong();
                        ParseRelationData(r, osmFile.ReadSubtree());

                        if (r.tags.Any(t => t.k == "type" && t.v == "restriction"  //This relation is a restriction on a road, not interesting
                            || t.k == "building")  //this is a building and we're not looking for buildings right now.
                            || r.members.Count == 0 //we didn't add any of the ways in this relation because they weren't useful.
                            ) 
                        {
                            //do nothing with this entry.
                        }
                        else
                            relations.Add(r);
                        break;
                }
            }

            int counter = 0;
            //osmFile.Save(filename); //saves our deletions to speed up future runs. Not really needed with XmlReader
        }

        public static List<Tag> parseTags(XmlReader xr)
        {
            List<Tag> retVal = new List<Tag>();

            while (xr.Read())
            {
                if (xr.Name == "tag")
                {
                    Tag t = new Tag() { k = xr.GetAttribute("k"), v = xr.GetAttribute("v") };
                    if (t.k.StartsWith("name") && t.k != "name" //purge non-English names.
                        || t.k == "created_by" //Don't need to monitor OSM users.
                        || t.k == "highway" //not interested in storing road data.
                        || t.k == "surface" //dont care, this is road data
                        || t.k == "traffic_calming" //dont care, this is road data
                        || t.k == "direction" //dont care, this is road data
                        )
                    { 
                        //Ignore this tag.
                    }
                    else
                        retVal.Add(t);
                }
            }
            return retVal;
        }

        public static void ParseWayData(Way w, XmlReader xr)
        {
            List<Node> retVal = new List<Node>();

            while (xr.Read())
            {
                if (xr.Name == "nd")
                {
                    long nodeID = xr.GetAttribute("ref").ToLong();
                    Node n = nodeLookup[nodeID].First(); // nodes.Where(n => n.id == nodeID).FirstOrDefault();
                    if (n != null && n.id != null)
                        w.nds.Add(n);
                }
                else if (xr.Name == "tag")
                {
                    Tag t = new Tag() { k = xr.GetAttribute("k"), v = xr.GetAttribute("v") };
                    if (t.k.StartsWith("name") && t.k != "name" //purge non-English names.
                        || t.k == "created_by" //Don't need to monitor OSM users.
                        )
                    {
                        //Ignore this tag.
                    }
                    else
                        w.tags.Add(t);
                }
            }
            return;
        }

        public static void ParseRelationData(Relation r, XmlReader xr)
        {
            List<Node> retVal = new List<Node>();
            
            while (xr.Read())
            {
                if (xr.Name == "member")
                {
                    var memberType = xr.GetAttribute("type");
                    if (memberType == "way") //get ways, ignore nodes for now.
                    {
                        long wayID = xr.GetAttribute("ref").ToLong();
                        if (wayLookup[wayID].Count() > 0) //we might not have added this way if it was't interesting.
                        {
                            Way w = wayLookup[wayID].First();
                            if (w != null && w.id != null)
                                r.members.Add(w);
                        }
                    }
                }
                else if (xr.Name == "tag")
                {
                    Tag t = new Tag() { k = xr.GetAttribute("k"), v = xr.GetAttribute("v") };
                    if (t.k.StartsWith("name") && t.k != "name" //purge non-English names.
                        || t.k == "created_by" //Don't need to monitor OSM users.
                        )
                    {
                        //Ignore this tag.
                    }
                    else
                        r.tags.Add(t);
                }
            }
            return;
        }


        //This works with XmlDocument, but XmlReader is so much faster.
        //public static List<Tag> parseTags(XmlNode xmlNode)
        //{
        //    List<Tag> retVal = new List<Tag>();
        //    List<XmlNode> stuffToRemove = new List<XmlNode>();
        //    foreach (XmlNode t in xmlNode.ChildNodes)
        //    {
        //        if (t.Name == "tag")
        //        {
        //            //TODO: delete boring/space consuming tags from the node here
        //            if (t.Attributes["k"].Value.StartsWith("name") && t.Attributes["k"].Value != "name" //purge non-English names.
        //                || t.Attributes["k"].Value == "created_by"
        //                )
        //                stuffToRemove.Add(t);
        //            else
        //            {
        //                retVal.Add(new Tag() { k = t.Attributes["k"].Value, v = t.Attributes["v"].Value });
        //            }
        //        }
        //    }
        //    foreach (XmlNode x in stuffToRemove)
        //        xmlNode.RemoveChild(x);

        //    return retVal;
        //}
    }
}
