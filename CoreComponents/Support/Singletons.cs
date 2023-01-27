using NetTopologySuite;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Prepared;
using System;
using System.Collections.Generic;
using System.Linq;
using static PraxisCore.DbTables;

namespace PraxisCore
{
    public static class Singletons
    {
        public static GeometryFactory factory = NtsGeometryServices.Instance.CreateGeometryFactory(4326);
        public static PreparedGeometryFactory pgf = new PreparedGeometryFactory();
        public static bool SimplifyAreas = false;

        //A * value is a wildcard that means any value counts as a match for that key. If the tag exists, its value is irrelevant. Cannot be used in NOT checks.
        //A * key is a rule that will not match based on tag values, as no key will == *. Used for backgrounds and special styles called up by name.
        //types vary:
        //any: one of the pipe delimited values in the value is present for the given key.
        //equals: the one specific value exists on that key. Slightly faster than Any when matching a single key, but trivially so.
        //or: as long as one of the rules with or is true, accept this entry as a match. each Or entry should be treated as its own 'any' for parsing purposes.
        //not: this rule must be FALSE for the style to be applied.
        //default: always true.
        //New Layer Rules:
        //Roads 90-99
        //default content: 100

        /// <summary>
        /// The baseline set of TagParser styles. 
        /// </list>
        /// </summary>
        public static List<StyleEntry> defaultStyleEntries = new List<StyleEntry>();
    };
}
