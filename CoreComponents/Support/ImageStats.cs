using Google.OpenLocationCode;
using System;

namespace PraxisCore.Support
{
    /// <summary>
    /// Helper class to store some common values needed when generating images. Saving the area to draw and the resolution to draw it at allows for a lot of flexibilty
    /// </summary>

    public class ImageStats
    {
        /// <summary>
        /// The width of the output image in pixels
        /// </summary>
        public int imageSizeX { get; set; }
        /// <summary>
        /// the height of the output image in pixels
        /// </summary>
        public int imageSizeY { get; set; }
        /// <summary>
        /// Internal measurement value for determining what gets drawn
        /// </summary>
        public double degreesPerPixelX { get; set; }
        /// <summary>
        /// Internal measurement value for determining what gets drawn
        /// </summary>
        public double degreesPerPixelY { get; set; }
        /// <summary>
        /// When drawing, do not load items from the database that would take up fewer than this many pixels in the final image.
        /// </summary>
        public double filterSize { get; set; }
        /// <summary>
        /// Internal measurement value for determining what gets drawn
        /// </summary>
        public double pixelsPerDegreeX { get; set; }
        /// <summary>
        /// Internal measurement value for determining what gets drawn
        /// </summary>
        public double pixelsPerDegreeY { get; set; }
        
        /// <summary>
        /// The GeoArea contained in the image to be drawn.
        /// </summary>
        public GeoArea area { get; set; }

        public ImageStats(ReadOnlySpan<char> plusCode)
        {
            //Convenience method.
            area = plusCode.ToGeoArea();
            MapTileSupport.GetPlusCodeImagePixelSize(plusCode, out int x, out int y);

            imageSizeX = x;
            imageSizeY = y;

            degreesPerPixelX = area.LongitudeWidth / imageSizeX;
            degreesPerPixelY = area.LatitudeHeight / imageSizeY;

            pixelsPerDegreeX = imageSizeX / area.LongitudeWidth;
            pixelsPerDegreeY = imageSizeY / area.LatitudeHeight;

            filterSize = (degreesPerPixelY / ConstantValues.resolutionCell11Lat) * IMapTiles.GameTileScale;
        }


        /// <summary>
        /// Creates a new ImageStats for a given PlusCode based on the default settings for the app.
        /// Plus Codes are usually rendered at a 4:5 aspect ratio in Praxismapper due to defining a base pixel as an 11-char PlusCode
        /// </summary>
        /// <param name="plusCode">plus code to generate stats for. </param>
        public ImageStats(string plusCode)
        {
            //Convenience method.
            area = plusCode.ToGeoArea();
            MapTileSupport.GetPlusCodeImagePixelSize(plusCode, out int x, out int y);

            imageSizeX = x;
            imageSizeY = y;

            degreesPerPixelX = area.LongitudeWidth / imageSizeX;
            degreesPerPixelY = area.LatitudeHeight / imageSizeY;

            pixelsPerDegreeX = imageSizeX / area.LongitudeWidth;
            pixelsPerDegreeY = imageSizeY / area.LatitudeHeight;

            filterSize = (degreesPerPixelY / ConstantValues.resolutionCell11Lat) * IMapTiles.GameTileScale;
        }
        
        /// <summary>
        /// Creates a new ImageStats for a given GeoArea from a PlusCode to match the defined width and height. 
        /// Plus Codes are usually rendered at a 4:5 aspect ratio in Praxismapper due to defining a base pixel as an 11-char PlusCode
        /// </summary>
        /// <param name="geoArea">Decoded pluscode, or converted coordinates into a GeoArea</param>
        /// <param name="imageWidth">image width in pixels</param>
        /// <param name="imageHeight">image height in pixels</param>
        public ImageStats(GeoArea geoArea, int imageWidth, int imageHeight)
        {
            //Pluscode parameters
            imageSizeX = imageWidth;
            imageSizeY = imageHeight;

            area = geoArea;
            CalculateDimentions();
        }

        /// <summary>
        /// Creates a new ImageStats for a set of SlippyMap parameters. 
        /// Default slippy tiles are drawn at 512x512 versus the standard 256x256. Setting your Slippymap view's zoom offset to -1 creates an identical experience for the user.
        /// </summary>
        /// <param name="zoomLevel">Integer 2-20, per SlippyMap conventions</param>
        /// <param name="xTile">X coords of the requested tile</param>
        /// <param name="yTile">Y coords of the requested tile</param>
        /// <param name="imageSize">Image width in pixels. Usually 512 in PraxisMapper</param>
        public ImageStats(int zoomLevel, int xTile, int yTile, int imageSize)
        {
            //Slippy map parameters
            var n = Math.Pow(2, zoomLevel);

            var lon_degree_w = xTile / n * 360 - 180;
            var lon_degree_e = (xTile + 1) / n * 360 - 180;

            var lat_rads_n = Math.Atan(Math.Sinh(Math.PI * (1 - 2 * yTile / n)));
            var lat_degree_n = lat_rads_n * 180 / Math.PI;

            var lat_rads_s = Math.Atan(Math.Sinh(Math.PI * (1 - 2 * (yTile + 1) / n)));
            var lat_degree_s = lat_rads_s * 180 / Math.PI;

            area = new GeoArea(lat_degree_s, lon_degree_w, lat_degree_n, lon_degree_e);

            imageSizeX = imageSize;
            imageSizeY = imageSize;

            CalculateDimentions();
        }

        /// <summary>
        /// Recalcuate the internal values used to determine what gets drawn in the image. 
        /// </summary>
        public void CalculateDimentions()
        {
            degreesPerPixelX = area.LongitudeWidth / imageSizeX;
            degreesPerPixelY = area.LatitudeHeight / imageSizeY;

            pixelsPerDegreeX = imageSizeX / area.LongitudeWidth;
            pixelsPerDegreeY = imageSizeY / area.LatitudeHeight;

            filterSize = (degreesPerPixelY / ConstantValues.resolutionCell11Lat) * IMapTiles.GameTileScale;
        }

        /// <summary>
        /// Resize the requested image, given new maximum sizes in pixels. This scales proportionally, so the new image bounds may not match both maximums.
        /// </summary>
        /// <param name="maxX">the maximum width the image should scale to, in pixels</param>
        /// <param name="maxY">the maximum height the image should scale to, in pixels</param>
        public void ScaleToFit(int maxX, int maxY)
        {
            var xScale = maxX / (double)imageSizeX;
            var yScale = maxY / (double)imageSizeY;
            var useScale = Math.Min(xScale, yScale);

            imageSizeX = (int)(imageSizeX * useScale);
            imageSizeY = (int)(imageSizeY * useScale);

            CalculateDimentions();
        }
    }
}
