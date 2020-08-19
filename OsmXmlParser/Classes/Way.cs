using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OsmXmlParser.Classes
{
    public class Way
    {
        public long id { get; set; }
        public List<Node> nds { get; set; } = new List<Node>();
        public List<Tag> tags { get; set; } = new List<Tag>();
    }
}
