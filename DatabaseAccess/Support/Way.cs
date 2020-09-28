﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DatabaseAccess.Support
{
    public class Way
    {
        public long id { get; set; }
        public string name { get; set; }
        public List<Node> nds { get; set; } = new List<Node>(); //nodes, abbreviated
        public List<long> nodRefs { get; set; } = new List<long>(); //longs to identify which nodes we need.
        public List<Tag> tags { get; set; } = new List<Tag>();
        public string AreaType { get; set; } //holding this now to use for later classes as well.
    }
}