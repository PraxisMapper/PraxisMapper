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

        public static string PbfFolder = "";
        public static string JsonMapDataFolder = "";
        public static long FilesizeSplit = 400000000;
    }
}
