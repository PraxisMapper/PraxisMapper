using Google.OpenLocationCode;
using System;

namespace PraxisCore.Support
{
    /// <summary>
    /// Helper class to calculate some common values needed when generating images. Saving the area to draw and the resolution to draw it at allows for a lot of flexibilty
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
        /// When drawing, do not load items from the database with a DrawSizeHint under this value. The auto-calculated value works out to about 1 pixel on the final image.
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

        //TODO: potential addition: styleSet string, so that's part of the image here instead?
        //TODO: potential addition: image byte[], so we can keep it here? Or does that turn this into the MapTile class?

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

            filterSize = (degreesPerPixelY * degreesPerPixelX / ConstantValues.squareCell11Area) / MapTileSupport.GameTileScale;
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

            filterSize = (degreesPerPixelY / ConstantValues.resolutionCell11Lat) * MapTileSupport.GameTileScale;
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

            //DrawSizeHint is "how many Cell11s at GameTileScale is this?
            //FilterSize should be "how many Cell11s is one pixel of this image?"
            filterSize = (degreesPerPixelY * degreesPerPixelX / ConstantValues.squareCell11Area); // / MapTileSupport.GameTileScale;
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

        /// <summary>
        /// Given a GeoArea, adjusts values to ensure the entire area fits within the image's currently set size proportionally.
        /// </summary>
        /// <param name="newArea"></param>
        public void FitToImageSize(GeoArea newArea)
        {
            //This will create a new area with potentially different proportions. The goal is to get newArea to fit nicely inside the current image size,
            //and that may mean changing the dimentions so that its proportions fit.

            //This is harder than expected, possibly because of projection issues making things look worse after adjusting numbers
            //Or, am i off, and the fix is to make the smaller side larger, since its already squishing the big side to fit in the image?

            //plan: get aspect ratio, resize new area larger to match it
            var aspectRatioImage = (double)imageSizeX / (double)imageSizeY;
            var wider = imageSizeX > imageSizeY;
            if (aspectRatioImage < 1)
                aspectRatioImage = (double)imageSizeY / (double)imageSizeX;

            var aspectRatioArea = newArea.LongitudeWidth / newArea.LatitudeHeight;
            if (aspectRatioArea == aspectRatioImage)
            {
                area = newArea;
            }
            else
            {
                //figure out how much to increase the area by on a side to make it fit.
                // EX: i want a 250x250 image, area is proportionally 200x180 (actual area value less important, but we'll call it 20 x 18)
                // aspect ratio 1 vs 1.11
                //Step 1: take larger side of area, get size.  This is new size for that side. May not change.
                //Step 2: divide smaller side of area by IMAGE aspect ratio. This is new proportional size for area. 
                //If centered, this should be correct.
                //EX 2: for 20x18 area, becomes 20x20, (so we're at a 1:1 aspect ratio), and then need to divide the original smaller side by image aspect ratio (1, so no changes here)
                //EX 2: fit 20x18 area to a 300x250 pixel box (1.2 ratio): box becomes 20x20, then becomes 20x16.666 (1.2), so decimals do matter.
                // -BUT this doesn't cover the original area! so we need to multiply both values by the aspect ratio?
                // - 20x20 * 1.2 = 24x24. NOW we divide smaller by aspect ratio to get 24x20, which IS 1.2 aspect ratio and covers the whole area.

                //or is this overcomplicating it? Smaller size needs multiplied to fit the proportional area, then larger size needs multiplied up to whatever.
                //we have to remember the original aspect ratio, thats probably part of my issue here. 

                var originalWider = newArea.LongitudeWidth > newArea.LatitudeHeight;
                var newSquareSize = originalWider ? newArea.LongitudeWidth : newArea.LatitudeHeight;
                var newLongerSize = originalWider ? newArea.LongitudeWidth * aspectRatioImage : newArea.LatitudeHeight * aspectRatioImage;

                newSquareSize = newSquareSize / 2;
                newLongerSize = newLongerSize / 2;

                if (wider)
                    area = new GeoArea(newArea.CenterLatitude - newSquareSize, newArea.CenterLongitude - newLongerSize, newArea.CenterLatitude + newSquareSize, newArea.CenterLongitude + newLongerSize);
                else
                    area = new GeoArea(newArea.CenterLatitude - newLongerSize, newArea.CenterLongitude - newSquareSize, newArea.CenterLatitude + newLongerSize, newArea.CenterLongitude + newSquareSize);

            }
            CalculateDimentions();
        }
    }
}