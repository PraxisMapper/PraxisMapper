using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CoreComponents
{
    public static class PaintTown
    {
        public static string LearnCell8(int instanceID, string Cell8)
        {
            //Which factions own which Cell10s nearby?
            var db = new PraxisContext();
            var cellData = db.PaintTownEntries.Where(t => t.PaintTownConfigId == instanceID && t.Cell8 == Cell8).ToList();
            string results = ""; //Cell8 + "|";
            foreach (var cell in cellData)
                results += cell.Cell10 + "=" + cell.FactionId + "|";
            return results;
        }
    }
}
