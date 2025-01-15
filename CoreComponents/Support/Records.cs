using NetTopologySuite.Geometries;
using OsmSharp.Tags;
using System;
using System.Collections.Generic;
using static PraxisCore.DbTables;

namespace PraxisCore.Support
{
    //records are new C# 9.0 shorthand for an immutable class (only edited on creation). C# 10 adds record structs, which are value types instead of reference types and a bit faster in some cases..
    public record struct CoordPair(float lat, float lon); //Used only in TestPerfApp
    public record struct CompletePaintOp(Geometry elementGeometry, double drawSizeHint, StylePaint paintOp, string tagValue, double lineWidthPixels, bool OverrideColor);
    public readonly record struct AreaInfo(string name, string style, Guid privacyId);
    public readonly record struct AreaDetail(string plusCode, AreaInfo data);
    public readonly record struct AreaDetailAll(string plusCode, List<AreaInfo> data);

    //This one will replace the OsmCompleteGeo class and slightly boost performance reading PBF files.
    public record class FundamentalOsm(Coordinate[][] outers, Coordinate[][] inners, TagsCollection tags, int entryType, long entryId);

}
