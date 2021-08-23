using System.Collections.Generic;
using NetTopologySuite;
using NetTopologySuite.Geometries;
using static CoreComponents.DbTables;

namespace CoreComponents.Support
{
    //records are new C# 9.0 shorthand for an immutable class (only edited on creation).
    public record MapDataForJson(string name, string place, string type, long? WayId, long? NodeId, long? RelationId, int AreaTypeId, double? AreaSize); //used for serializing MapData, since Geography types do not serialize nicely.

    public record CoordPair(float lat, float lon); //Used only in TestPerfApp

    public record StoredOsmElementForJson(long id, string name, long sourceItemID, int sourceItemType, string elementGeometry, string WayTags, bool IsGameElement, bool isUserProvided, bool isGenerated);

    public record CompletePaintOp(Geometry elementGeometry, double areaSize, TagParserPaint paintOp);

}
