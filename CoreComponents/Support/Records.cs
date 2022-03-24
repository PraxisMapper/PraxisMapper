using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;
using static PraxisCore.DbTables;
using static PraxisCore.StandaloneDbTables;

namespace PraxisCore.Support
{
    //records are new C# 9.0 shorthand for an immutable class (only edited on creation).
    public record struct CoordPair(float lat, float lon); //Used only in TestPerfApp
    public record struct CompletePaintOp(Geometry elementGeometry, double areaSize, StylePaint paintOp, string tagValue, double lineWidthPixels);
    public record struct CustomDataAreaResult(string plusCode, string key, string value);
    public record struct CustomDataPlaceResult(Guid elementId, string key, string value);
    public record struct CustomDataPlayerResult(string deviceId, string key, string value);
    public readonly record struct TerrainData(string Name, string areaType, Guid PrivacyId);
    public readonly record struct FindPlaceResult(string plusCode, TerrainData data);
    public readonly record struct FindPlacesResult(string plusCode, List<TerrainData> data);

}
