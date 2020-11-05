using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CoreComponents.Support
{
    //records are new C# 9.0 shorthand for an immutable class (only edited on creation).
    public record NodeData(long id, float lat, float lon); //Stores in a Way to hold lat/long for each point in it.

    public record NodeReference(long Id, float lat, float lon, string name, string type); //holds only the node data relevant to the application.

    public record MapDataForJson(string name, string place, string type, long? WayId, long? NodeId, long? RelationId, int AreaTypeId); //used for serializing MapData, since Geography types do not serialize nicely.

    public record CoordPair(float lat, float lon);

    public record RelationMemberData(long Id, string name, string type);
}
