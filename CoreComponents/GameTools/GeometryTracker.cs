using NetTopologySuite.Geometries;
using System.Text.Json.Serialization;

namespace PraxisCore.GameTools {
    /// <summary>
    /// GeometryTracker allows you to save PlusCode Cells to an accumulating Geometry object. This is useful for drawing a map of team-controlled territory, a player's complete
    /// location history, or other things not tied to specific Places on the map.
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
        public string exploredAsText { get { return explored.ToText(); } } //This is what gets saves as JSON to our database for simplicity, even if it incurs some processing overhead.
        /// <summary>
        /// Internal variable, used to skip populating the Geometry object if it's already been filled in.
        /// </summary>
        bool isPopulated = false;

        /// <summary>
        /// Fills in explored with the data from exploredAsText, if it has not yet been populated since loading from the database.
        /// </summary>
        public void PopulateExplored() 
        {
            if (!isPopulated) 
            {
                explored = GeometrySupport.GeometryFromWKT(exploredAsText);
                isPopulated = true;
            } 
        }

        /// <summary>
        /// Add a PlusCode cell to the explored geometry. Can be any valid PlusCode size, though if you are tracking Cell11s consider using RecentPath instead of GeometryTracker.
        /// </summary>
        /// <param name="plusCode">A valid PlusCode (without the +, if its 10 digits or longer)</param>
        public void AddCell(string plusCode) 
        {
            PopulateExplored();
            //Lines that touch remain multipolygons. Unioning buffered areas leaves all their points in place. Simplify removes most redundant points.
            explored = explored.Union(GeometrySupport.MakeBufferedGeoArea(plusCode.ToGeoArea(), 0.00000001).ToPolygon());
        }

        /// <summary>
        /// Removes a PlusCode cell from the explored geometry. Can be any valid PlusCode size, though if you are tracking Cell11s consider using RecentPath instead of GeometryTracker.
        /// </summary>
        public void RemoveCell(string plusCode) 
        {
            PopulateExplored();
            explored = explored.Difference(GeometrySupport.MakeBufferedGeoArea(plusCode.ToGeoArea(), 0.00000001).ToPolygon()).Simplify(0.00000001);
        }

        /// <summary>
        /// Add the given geometry object's area to the tracker's geometry.
        /// </summary>
        /// <param name="geo"></param>
        public void AddGeometry(Geometry geo)
        {
            PopulateExplored();
            explored = explored.Union(geo).Simplify(0.00000001);
        }

        /// <summary>
        /// Removes the given geometry object's area to the tracker's geometry.
        /// </summary>
        public void RemoveGeometry(Geometry geo)
        {
            PopulateExplored();
            explored = explored.Difference(geo).Simplify(0.00000001);
        }

    }
}
