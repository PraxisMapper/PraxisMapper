namespace CoreComponents.Support
{
    //records are new C# 9.0 shorthand for an immutable class (only edited on creation).
    public record NodeReference(long Id, float lat, float lon, string name, string type); //holds only the node data relevant to the application.

    public record MapDataForJson(string name, string place, string type, long? WayId, long? NodeId, long? RelationId, int AreaTypeId, double? AreaSize); //used for serializing MapData, since Geography types do not serialize nicely.

    public record CoordPair(float lat, float lon); //Used only in TestPerfApp

    public record RelationMemberData(long Id, string name, string type);

    public record StoredOsmElementForJson(long id, string name, long sourceItemID, int sourceItemType, string elementGeometry, string WayTags, bool IsGameElement);
}
