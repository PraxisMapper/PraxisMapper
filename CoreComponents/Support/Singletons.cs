using NetTopologySuite;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Prepared;
using System.Collections.Generic;
using static PraxisCore.DbTables;

namespace PraxisCore {
    public static class Singletons
    {
        public static GeometryFactory geometryFactory = NtsGeometryServices.Instance.CreateGeometryFactory(4326);
        public static PreparedGeometryFactory preparedGeometryFactory = new PreparedGeometryFactory();
        public static bool SimplifyAreas = false;

        /// <summary>
        /// The baseline set of TagParser styles. 
        /// </summary>
        public static List<StyleEntry> defaultStyleEntries = new List<StyleEntry>();
    };
}
