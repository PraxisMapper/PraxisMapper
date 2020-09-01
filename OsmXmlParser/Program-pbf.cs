using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using OsmSharp;
using OsmSharp.Geo;
using OsmSharp.Streams;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace OsmXmlParser
{
    public static class Program_pbf
    {
        //TODO: read the osm binary format. it's much smaller, and might be more efficient than reading XML.
        //The planet.osm file is 300-400GB, versus the 960GB all the XML files are. But I have to read this one in chunks, and the format is not as clear.
        //There's packages for this. I should use those. This isn't even necessary, but it'll make future work easier.

        public static List<string> relevantTourismValues = new List<string>() { "artwork", "attraction", "gallery", "museum", "viewpoint", "zoo" }; //The stuff we care about in the tourism category. Zoo and attraction are debatable.
        public static void testPbfRead()
        {
            
            using (var fs = File.OpenRead(@"D:\Projects\OSM Server Info\us-midwest-latest.osm.pbf"))
            {
                var source = new PBFOsmStreamSource(fs);

                var progress = source.ShowProgress();

                //filter out data here
                //Now this is my default filter.
                var filteredWays = progress.Where(p => p.Type == OsmSharp.OsmGeoType.Way && // || p.Type == OsmSharp.OsmGeoType.Node &&
                        (p.Tags.Contains("natural", "water")  ||
                        p.Tags.Contains("natural", "wetlands") ||
                        p.Tags.Contains("leisure", "park") ||
                        p.Tags.Contains("natural", "beach") ||
                        p.Tags.Contains("leisure", "beach_resort") ||
                        p.Tags.Contains("amenity", "university") ||
                        p.Tags.Contains("amenity", "college") ||
                        p.Tags.Contains("leisure", "nature_reserve") ||
                        p.Tags.Contains("landuse", "cemetery") ||
                        p.Tags.Contains("amenity", "grave_yard") ||
                        p.Tags.Contains("shop", "mall") ||
                        p.Tags.Contains("landuse", "retail") ||
                        p.Tags.Any(t => t.Key == "historical") ||
                        p.Tags.Any(t => t.Key == "tourism" && relevantTourismValues.Contains(t.Value))
                        ))
                    .Select(w => (Way)w)
                    .ToList();

                var nodesToGet = filteredWays.SelectMany(w => w.Nodes).ToLookup(k => k, v => v); //Same logic as XML fil

                var filteredNodes = progress.Where(p => p.Type == OsmSharp.OsmGeoType.Node && // || p.Type == OsmSharp.OsmGeoType.Node &&
                        (nodesToGet.Contains(p.Id.GetValueOrDefault()) || (
                        p.Tags.Contains("natural", "water") ||
                        p.Tags.Contains("natural", "wetlands") ||
                        p.Tags.Contains("leisure", "park") ||
                        p.Tags.Contains("natural", "beach") ||
                        p.Tags.Contains("leisure", "beach_resort") ||
                        p.Tags.Contains("amenity", "university") ||
                        p.Tags.Contains("amenity", "college") ||
                        p.Tags.Contains("leisure", "nature_reserve") ||
                        p.Tags.Contains("landuse", "cemetery") ||
                        p.Tags.Contains("amenity", "grave_yard") ||
                        p.Tags.Contains("shop", "mall") ||
                        p.Tags.Contains("landuse", "retail") ||
                        p.Tags.Any(t => t.Key == "historical") ||
                        p.Tags.Any(t => t.Key == "tourism" && relevantTourismValues.Contains(t.Value))
                        )))
                    .Select(w => (Node)w)
                    .ToList();


                //This is the official example, but throws errors for me.
                //turn data into a usable stream here
                var features = filteredWays.ToFeatureSource();

                //only pull out closed shapes for now
                var areas = features.Where(f => f.Geometry is Polygon); // or Point

                //save items
                var collection = new FeatureCollection();
                foreach (var a in areas) //This keeps throwing errors about not finding a constructor?
                    collection.Add(a);

                //could now write Json for this
                //Takes about 6:30 to read to US-Midwest.pbf
                int b = 1;
            }

        }

        


    }
}
