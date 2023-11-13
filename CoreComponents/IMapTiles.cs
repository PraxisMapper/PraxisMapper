using Google.OpenLocationCode;
using PraxisCore.Support;
using System.Collections.Generic;
using static PraxisCore.DbTables;

namespace PraxisCore
{
    public interface IMapTiles
    {
        /// <summary>
        /// Tells the MapTiles object to create and cache frequently-used objects for drawing later.
        /// </summary>
        public void Initialize();
        

        /// <summary>
        /// Using the Area in stats, draw the map tile for the given Places using styleSet's rules
        /// </summary>
        /// <param name="stats"></param>
        /// <param name="drawnItems"></param>
        /// <param name="styleSet"></param>
        /// <returns></returns>
        public byte[] DrawAreaAtSize(ImageStats stats, List<DbTables.Place> drawnItems = null, string styleSet = "mapTiles", string skipType = null);
        /// <summary>
        /// Draws the Area in stats using the list of given PaintOps.
        /// </summary>
        /// <param name="stats"></param>
        /// <param name="paintOps"></param>
        /// <returns></returns>
        public byte[] DrawAreaAtSize(ImageStats stats, List<CompletePaintOp> paintOps);
        /// <summary>
        /// (SkiaSharp Only, beta) Draw the given area as an SVG files instead of a PNG.
        /// </summary>
        /// <param name="stats"></param>
        /// <param name="drawnItems"></param>
        /// <param name="styles"></param>
        /// <param name="filterSmallAreas"></param>
        /// <returns></returns>
        public string DrawAreaAtSizeSVG(ImageStats stats, List<DbTables.Place> drawnItems = null, Dictionary<string, StyleEntry> styles = null, bool filterSmallAreas = true);
        /// <summary>
        /// Given 2 mapTiles, layer then on top of each other in the given Area.
        /// </summary>
        /// <param name="info"></param>
        /// <param name="bottomTile"></param>
        /// <param name="topTile"></param>
        /// <returns></returns>
        public byte[] LayerTiles(ImageStats info, byte[] bottomTile, byte[] topTile);
    }
}
