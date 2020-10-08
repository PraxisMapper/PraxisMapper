using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DatabaseAccess.Support
{
    //records are new C# 9.0 shorthand for an immutable class (only edited on creation).
    
    public record NodeReference(long Id, double lat, double lon, string name, string type); //holds only the node data relevant to the application.

    public record MapDataForJson(long MapDataId, string name, string place, string type, long? WayId, long? NodeId, long? RelationId, int AreaTypeId); //used for serializing MapData, since Geography types do not serialize nicely.

    public record CoordPair(double lat, double lon);
}
