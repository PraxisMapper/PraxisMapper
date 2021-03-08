using System.Collections.Generic;
using System.Linq;
using static CoreComponents.DbTables;

namespace CoreComponents
{
    public static class PaintTown //Core logic for Paint The Town moved here, to assist in decoupling it from the MVC controller if someone wants to re-implement a frontend.
    {
        public static List<PaintTownEntry> LearnCell8(int instanceID, string Cell8)
        {
            //Which factions own which Cell10s nearby?
            var db = new PraxisContext();
            var cellData = db.PaintTownEntries.Where(t => t.PaintTownConfigId == instanceID && t.Cell8 == Cell8).ToList();
            return cellData;
        }
    }
}
