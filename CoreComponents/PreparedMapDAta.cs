using NetTopologySuite.Geometries.Prepared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CoreComponents
{
    public class PreparedMapData
    {
        //The Minimum stuff to use PreparedGeometry for intersection checks.
        public long PreparedMapDataID;
        public IPreparedGeometry place;
        public int AreaTypeId;
    }
}
