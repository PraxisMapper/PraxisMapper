using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OsmXmlParser
{
    public static class ParserSettings
    {
        //Do multiple passes on all pbf files regardless of size.
        public static bool ForceSeparateFiles = false;

        //TODO: load options from file somewhere.
        public static string PbfFolder = @"D:\Projects\OSM Server Info\XmlToProcess\";
        public static string JsonMapDataFolder = @"D:\Projects\OSM Server Info\Trimmed JSON Files\";
        public static long FilesizeSplit = 400000000;

        //If true, don't round locations to 7 points and don't simplify entries during processing.
        public static bool ForceHighAccuracy = false;
    }
}
