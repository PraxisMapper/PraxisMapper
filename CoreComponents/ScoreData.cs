﻿using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;
using System.Linq;
using static PraxisCore.ConstantValues;
using static PraxisCore.DbTables;

namespace PraxisCore
{
    /// <summary>
    /// A default implementation for scoring locations. 1 point per 10-digit PlusCode in length or area, minimum of 1.
    /// </summary>
    public static class ScoreData
    {
        //TODO: these might be slightly faster with StringBuilder during calculations than String.Join after doing all the calculation. Test and check that assumption.
        //Default Scoring rules:
        //Each Cell10 of surface area is 1 Score (would be Points in any other game, but Points is already an overloaded term in this system).
        //OSM Areas are measured in square area, divided by Cell10 area squared. (An area that covers 25 square Cell10s is 25 Score)
        //Lines are measured in their length.  (A trail that's 25 * resolutionCell10 long is 25 Score)
        //OSM Points (single lat/lon pair) are assigned a Score of 1 as the minimum interactable size object. 

        /// <summary>
        /// Get a list of element names, score, and client-facing IDs from a list of places and an area to search, cropped to that area.
        /// </summary>
        /// <param name="areaPoly">the area to search and use to determine scores of elements intersecting it  </param>
        /// <param name="places">the elements to be scored, relative to their size in the given area</param>
        /// <returns>a string of pipe-separated values (name, score, ID) split by newlines</returns>
        public static string GetScoresForArea(Geometry areaPoly, List<StoredOsmElement> places)
        {
            //Determines the Scores for the Places, limited to the intersection of the current Area. 1 Cell10 = 1 Score.
            //EX: if a park overlaps 800 Cell10s, but the current area overlaps 250 of them, this returns 250 for that park.
            //Lists each Place and its corresponding Score.
            List<Tuple<string, long, Guid>> areaSizes = new List<Tuple<string, long, Guid>>();
            foreach (var md in places)
            {
                var containedArea = md.elementGeometry.Intersection(areaPoly);
                var areaCell10Count = GetScoreForSinglePlace(containedArea);
                areaSizes.Add(new Tuple<string, long, Guid>(TagParser.GetPlaceName(md.Tags), areaCell10Count, md.privacyId));
            }
            return string.Join(Environment.NewLine, areaSizes.Select(a => a.Item1 + "|" + a.Item2 + "|" + a.Item3));
        }

        /// <summary>
        /// Scores the list of places given in full
        /// </summary>
        /// <param name="places">The places to report a score for each.</param>
        /// <returns>a string of names and scores, </returns>
        public static string GetScoresForFullArea(List<StoredOsmElement> places)
        {
            //As above, but counts the Places' full area, not the area in the given Cell8 or Cell10. 
            List<Tuple<string, long, Guid>> areaSizes = new List<Tuple<string, long, Guid>>();
            foreach (var place in places)
            {
                areaSizes.Add(Tuple.Create(TagParser.GetPlaceName(place.Tags), GetScoreForSinglePlace(place.elementGeometry), place.privacyId));
            }
            return string.Join(Environment.NewLine, areaSizes.Select(a => a.Item1 + "|" + a.Item2 + "|" + a.Item3));
        }

        /// <summary>
        /// Given a single place, determines its full score. Polygons use their area, lines use their length, Points are assigned 1.
        /// </summary>
        /// <param name="place">The place to be scored</param>
        /// <returns>a long of the place's area or length, in Cell10s. Minimum of 1.</returns>
        public static long GetScoreForSinglePlace(Geometry place)
        {
            //Despite saving AreaSize to the database, this function isn't obsolete.
            //This code specifically will calculate the correct score based on the place's GeometryType.
            //Points are always 1. Lines are scores by how many Cell10s long they are. Areas are 1 per square Cell10 they cover. AreaSize doesn't distinguish between the 3, so I still calculate it here.
            //The core function for scoring.
            var containedAreaSize = place.Area; //The area, in square degrees
            if (containedAreaSize == 0)
            {
                //This is a line or a point, it has no area so we need to fix the calculations to match the display grid.
                //Points will always be 1.
                //Lines will be based on distance.
                if (place is NetTopologySuite.Geometries.Point)
                    containedAreaSize = squareCell10Area;
                else if (place is NetTopologySuite.Geometries.LineString)
                    containedAreaSize = ((LineString)place).Length * resolutionCell10;
                //This gives us the length of the line in Cell10 lengths, which may be slightly different from the number of Cell10 draws on the map as belonging to this line.
            }
            var containedAreaCell10Count = (int)Math.Round(containedAreaSize / squareCell10Area);
            if (containedAreaCell10Count == 0)
                containedAreaCell10Count = 1;

            return containedAreaCell10Count;
        }

        /// <summary>
        /// Get the score for an area by its client facing ID
        /// </summary>
        /// <param name="elementId">the PrivacyID of a StoredOsmElement to be scored</param>
        /// <returns>the score for the requested StoredOsmElement</returns>
        public static long GetScoreForSinglePlace(Guid elementId)
        {
            var db = new PraxisContext();
            var place = db.StoredOsmElements.FirstOrDefault(e => e.privacyId == elementId).elementGeometry;
            return GetScoreForSinglePlace(place);
        }
    }
}
