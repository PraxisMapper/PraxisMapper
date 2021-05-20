namespace CoreComponents.Support
{
    //records are new C# 9.0 shorthand for an immutable class (only edited on creation).
    public record NodeData(long id, float lat, float lon); //Stores in a Way to hold lat/long for each point in it.

    public record NodeReference(long Id, float lat, float lon, string name, string type); //holds only the node data relevant to the application.

    public record MapDataForJson(string name, string place, string type, long? WayId, long? NodeId, long? RelationId, int AreaTypeId, double? AreaSize); //used for serializing MapData, since Geography types do not serialize nicely.

    public record CoordPair(float lat, float lon);

    public record RelationMemberData(long Id, string name, string type);

    //for investigating if its faster to return the places and their Cel10 entries instead of a Cell10 and its properties
    public record Cell10Info(string placeName, string Cell10, int areaTypeId);

    //V4 types. WayGeometry and WayTags get serialized somewhere.
    public record StoredWayForJson(long id, string name, long sourceItemID, int sourceItemType, string wayGeometry, string WayTags, bool IsGameElement);
}
