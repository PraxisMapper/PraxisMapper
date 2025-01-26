using NetTopologySuite.Geometries;
using System.Collections.Generic;

namespace PraxisCore.GameTools
{
    /// <summary>
    /// CellTracker allows you to save PlusCode Cell10 (or Cell11) values to be drawn to a geometry object later. This is useful for drawing a map of team-controlled territory, a player's complete
    /// location history, or other things not tied to specific Places on the map.
    /// If this is used for a player's location history, it MUST be saved with SetSecurePlayerData.
    /// This is dramatically faster than GeometryTracker when only drawing PlusCode cells as squares. For arbitrary geometries, GeometryTracker is required.
    /// </summary>
    public sealed class CellTracker {
        public Dictionary<string, byte> Visited { get; set; } = new Dictionary<string, byte>();

        /// <summary>
        /// Add a PlusCode cell to the explored geometry. Can be any valid PlusCode size, though if you are tracking Cell11s consider using RecentPath instead of GeometryTracker.
        /// </summary>
        /// <param name="plusCode">A valid 10 or 11 digit PlusCode</param>
        public void AddCell(string plusCode) 
        {
            Visited.TryAdd(plusCode, 1);
        }

        /// <summary>
        /// Removes a PlusCode cell from the explored geometry. Can be any valid PlusCode size, though if you are tracking Cell11s consider using RecentPath instead of GeometryTracker.
        /// </summary>
        public void RemoveCell(string plusCode) 
        {
            Visited.Remove(plusCode);
        }

        public Geometry AsGeometry()
        {
            //Converts this to a geometry object for drawing purposes.
            Geometry geo = Polygon.Empty;
            List<Geometry> subItems = new List<Geometry>();

            foreach(var entry in Visited.Keys)
                subItems.Add(entry.ToPolygon());

            geo = NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(subItems);
            return geo;
        }
    }
}
