using OsmSharp.Streams;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Larry
{
    public static class PbfRawDump
    {
        //Load a PBF file, save everything there to the DB directly.
        public static void DumpToDb(string filepath)
        {
            var db = new CoreComponents.PraxisContext();
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var stream = new System.IO.FileStream(filepath, System.IO.FileMode.Open);
            var source = new PBFOsmStreamSource(stream);
            var counter = 0;
            var saveCount = 0;
            foreach(var entry in source)
            {
                switch (entry.Type)
                {
                    case OsmSharp.OsmGeoType.Node:
                        var item = (OsmSharp.Node)entry;
                        var n = new CoreComponents.DbTables.StoredNode();
                        n.id = item.Id.Value;
                        n.lat = item.Latitude.Value;
                        n.lon = item.Longitude.Value;
                        n.NodeTags = item.Tags.Select(t => new CoreComponents.DbTables.NodeTags() { Key = t.Key, Value = t.Value }).ToList();
                        db.StoredNodes.Add(n);
                        //db.SaveChanges();
                        break;
                    case OsmSharp.OsmGeoType.Way:
                        var way = (OsmSharp.Way)entry;
                        var w = new CoreComponents.DbTables.StoredWay();
                        w.id = way.Id.Value;
                        //w.Nodes = way.Nodes;
                        w.WayTags = way.Tags.Select(t => new CoreComponents.DbTables.WayTags() { Key = t.Key, Value = t.Value }).ToList();
                        db.StoredWays.Add(w);
                        //db.SaveChanges();
                        break;
                    case OsmSharp.OsmGeoType.Relation:
                        var relation = (OsmSharp.Relation)entry;
                        var r = new CoreComponents.DbTables.StoredRelation();
                        r.id = relation.Id.Value;
                        r.RelationTags = relation.Tags.Select(t => new CoreComponents.DbTables.RelationTags() { Key = t.Key, Value = t.Value }).ToList();
                        db.StoredRelations.Add(r);
                        //db.SaveChanges();
                        break;
                }
                counter++;
                if (counter > 3000)
                {
                    counter = 0;
                    saveCount++;
                    Console.WriteLine("Saving 3,000 PBF entry chunk to DB (Save #" + saveCount + ")");
                    db.SaveChanges();
                    //db = new CoreComponents.PraxisContext(); //Avoids EF slowing down over time. No, use AutoDetectChanges = false
                }
            }

            db.SaveChanges();
        } //Delaware, a 16MB PBF, takes ~250MB space and ~14 minutes to process this way.

    }
}
