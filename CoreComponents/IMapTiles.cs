using Google.OpenLocationCode;
using NetTopologySuite.Geometries;
using PraxisCore.Support;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static PraxisCore.DbTables;

namespace PraxisCore
{
    internal interface IMapTiles
    {
        public static int MapTileSizeSquare;
        public static double GameTileScale;
        public static double bufferSize;

        public abstract GeoArea MakeBufferedGeoArea(GeoArea original);
        public abstract byte[] DrawOfflineEstimatedAreas(ImageStats info, List<StoredOsmElement> items);
        public abstract byte[] DrawCell8GridLines(GeoArea totalArea);
        public abstract byte[] DrawCell10GridLines(GeoArea totalArea);
        public abstract void ExpireMapTiles(Geometry g, string styleSet = "");
        public abstract void ExpireMapTiles(Guid elementId, string styleSet = "");
        public abstract void ExpireSlippyMapTiles(Geometry g, string styleSet = "");
        public abstract void ExpireSlippyMapTiles(Guid elementId, string styleSet = "");
        public abstract byte[] DrawUserPath(string pointListAsString);
        public abstract void GetPlusCodeImagePixelSize(string code, out int X, out int Y); //could be in a different class since this isnt drawing specific
        public abstract byte[] DrawPlusCode(string area, string styleSet = "mapTiles");
        public abstract byte[] DrawPlusCode(string area, List<CompletePaintOp> paintOps, string styleSet = "mapTiles");
        public abstract byte[] DrawAreaAtSize(ImageStats stats, List<StoredOsmElement> drawnItems = null, string styleSet = null, bool filterSmallAreas = true);
        public abstract byte[] DrawAreaAtSize(ImageStats stats, List<CompletePaintOp> paintOps); //, SKColor bgColor);
        public abstract List<CompletePaintOp> GetPaintOpsForStoredElements(List<StoredOsmElement> elements, string styleSet, ImageStats stats);
        public abstract List<CompletePaintOp> GetPaintOpsForCustomDataElements(Geometry area, string dataKey, string styleSet, ImageStats stats);
        public abstract List<CompletePaintOp> GetPaintOpsForCustomDataPlusCodes(Geometry area, string dataKey, string styleSet, ImageStats stats);
        public abstract List<CompletePaintOp> GetPaintOpsForCustomDataPlusCodesFromTagValue(Geometry area, string dataKey, string styleSet, ImageStats stats);
        public abstract string DrawAreaAtSizeSVG(ImageStats stats, List<StoredOsmElement> drawnItems = null, Dictionary<string, TagParserEntry> styles = null, bool filterSmallAreas = true);
        public abstract void PregenMapTilesForArea(GeoArea areaToDraw, bool saveToFiles = false); //might be placeable elsewhere since this would call draw functions
        public abstract void PregenSlippyMapTilesForArea(GeoArea buffered, int zoomLevel); //same as above
        public abstract byte[] LayerTiles(ImageStats info, byte[] bottomTile, byte[] topTile); //DOES belong here, since this does work in the image library
    }
}
