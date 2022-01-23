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
    public interface IMapTiles
    {
        public static int MapTileSizeSquare;
        public static double GameTileScale;
        public static double bufferSize;
        public byte[] DrawOfflineEstimatedAreas(ImageStats info, List<StoredOsmElement> items);
        public byte[] DrawCell8GridLines(GeoArea totalArea);
        public byte[] DrawCell10GridLines(GeoArea totalArea);
        public byte[] DrawUserPath(string pointListAsString);
        public void GetPlusCodeImagePixelSize(string code, out int X, out int Y); //could be in a different class since this isnt drawing specific
        public byte[] DrawPlusCode(string area, string styleSet = "mapTiles");
        public byte[] DrawPlusCode(string area, List<CompletePaintOp> paintOps, string styleSet = "mapTiles");
        public byte[] DrawAreaAtSize(ImageStats stats, List<StoredOsmElement> drawnItems = null, string styleSet = null, bool filterSmallAreas = true);
        public byte[] DrawAreaAtSize(ImageStats stats, List<CompletePaintOp> paintOps); //, SKColor bgColor);
        public List<CompletePaintOp> GetPaintOpsForStoredElements(List<StoredOsmElement> elements, string styleSet, ImageStats stats);
        public List<CompletePaintOp> GetPaintOpsForCustomDataElements(Geometry area, string dataKey, string styleSet, ImageStats stats);
        public List<CompletePaintOp> GetPaintOpsForCustomDataPlusCodes(Geometry area, string dataKey, string styleSet, ImageStats stats);
        public List<CompletePaintOp> GetPaintOpsForCustomDataPlusCodesFromTagValue(Geometry area, string dataKey, string styleSet, ImageStats stats);
        public string DrawAreaAtSizeSVG(ImageStats stats, List<StoredOsmElement> drawnItems = null, Dictionary<string, TagParserEntry> styles = null, bool filterSmallAreas = true);
        public void PregenMapTilesForArea(GeoArea areaToDraw, bool saveToFiles = false); //might be placeable elsewhere since this would call draw functions
        public void PregenSlippyMapTilesForArea(GeoArea buffered, int zoomLevel); //same as above
        public byte[] LayerTiles(ImageStats info, byte[] bottomTile, byte[] topTile); //DOES belong here, since this does work in the image library
    }
}
