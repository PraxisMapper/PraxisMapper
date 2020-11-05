using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CoreComponents.Support
{
    public class WayData
    {
        public long id { get; set; }
        public string name { get; set; }
        public List<NodeData> nds { get; set; } = new List<NodeData>(); //nodes, abbreviated
        public List<long> nodRefs { get; set; } = new List<long>(); //longs to identify which nodes we need.
        public string AreaType { get; set; } //holding this now to use for later classes as well.
    }
}
