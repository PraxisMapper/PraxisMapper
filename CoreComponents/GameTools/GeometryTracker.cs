using NetTopologySuite.Geometries;
using System;
using System.Linq;
using System.Text.Json.Serialization;

namespace PraxisCore.GameTools {
    /// <summary>
    /// GeometryTracker allows you to save arbitrary Geometry shapes to an accumulating Geometry object. This is useful for drawing a map of team-controlled territory, places visited,
    /// or other random shapes. If only tracking PlusCode cells, use CellTracker for dramatically better performance.
    /// If this is used for a player's location history, it MUST be saved with SetSecurePlayerData.
    /// </summary>
    public sealed class GeometryTracker {

        /// <summary>
        /// The full Geometry covering the tracked area. May be a Polygon or Multipolygon, depending on if all submitted Cells are connected orthogonally.
        /// </summary>
        [JsonIgnore]
        public Geometry explored { get; set; } = Singletons.geometryFactory.CreatePolygon(); //This is the object most of the work will be done against

        /// <summary>
        /// The Well-Known Text (WKT) form of the explored geometry. This is persisted to the database and regenerated to the explored value when necessary.
        /// </summary>
        public string exploredAsText { get { return explored.ToText(); } set { explored = Singletons.geomTextReader.Read(value); } } //This is what gets saves as JSON to our database for simplicity, even if it incurs some processing overhead.

        /// <summary>
        /// Add the given geometry object's area to the tracker's geometry.
        /// </summary>
        /// <param name="geo"></param>
        public void AddGeometry(Geometry geo)
        {
            explored = explored.Union(geo).Simplify(0.00000001);
        }

        /// <summary>
        /// Removes the given geometry object's area to the tracker's geometry.
        /// </summary>
        public void RemoveGeometry(Geometry geo)
        {
            explored = explored.Difference(geo);
            if (explored is GeometryCollection ex)
            {
                explored = Singletons.geometryFactory.CreateMultiPolygon(ex.Geometries.Where(g => g.GeometryType == "Polygon").Select(g => (Polygon)g).ToArray());
                
            }
            explored = explored.Simplify(0.00000001);
        }

    }
}
