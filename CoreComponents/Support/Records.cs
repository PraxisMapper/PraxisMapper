using NetTopologySuite.Geometries;
using System;
using static PraxisCore.DbTables;

namespace PraxisCore.Support
{
    //records are new C# 9.0 shorthand for an immutable class (only edited on creation).
    public record CoordPair(float lat, float lon); //Used only in TestPerfApp
    public record CompletePaintOp(Geometry elementGeometry, double areaSize, StylePaint paintOp, string tagValue, double lineWidthPixels);
    public record CustomDataAreaResult(string plusCode, string key, string value);
    public record CustomDataPlaceResult(Guid elementId, string key, string value);
    public record CustomDataPlayerResult(string deviceId, string key, string value);

}
