using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OsmXmlParser.Classes
{
    public class Node
    {
        public long id { get; set; }
        public double lat { get; set; }
        public double lon { get; set; }
        //public List<Tag> tags { get; set; }
    }
}
