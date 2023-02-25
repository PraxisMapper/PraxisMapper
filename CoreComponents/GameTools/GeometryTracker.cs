using NetTopologySuite.Geometries;
using System.Text.Json.Serialization;

namespace PraxisCore.GameTools {
    public class GeometryTracker {
        
        [JsonIgnore]
        public Geometry explored { get; set; } = Singletons.geometryFactory.CreatePolygon(); //This is the object most of the work will be done against
        public string exploredAsText { get; set; } = ""; //This is what gets saves as JSON to our database for simplicity, even if it incurs some processing overhead.
        bool isPopulated = false;

        public void PopulateExplored() 
        {
            if (!isPopulated && !string.IsNullOrEmpty(exploredAsText)) 
            {
                explored = GeometrySupport.GeometryFromWKT(exploredAsText);
                isPopulated = true;
            }
        }

        public void AddCell(string plusCode) 
        {
            PopulateExplored();
            explored = explored.Union(plusCode.ToPolygon());
            exploredAsText = explored.ToText();
        }

        public void RemoveCell(string plusCode) 
        {
            PopulateExplored();
            explored = explored.Difference(plusCode.ToPolygon());
            exploredAsText = explored.ToText();
        }
    }
}
