using Google.OpenLocationCode;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CoreComponents.Support
{
    public class ImageStats
    {
        //a small helper class to calculate and reuse some common calculations
        //TODO: start passing this into converters to cut down the number of parameters used.
        public int imageSizeX { get; set; }
        public int imageSizeY { get; set; }
        public double degreesPerPixelX { get; set; }
        public double degreesPerPixelY { get; set; }
        public bool drawPoints { get; set; }
        public double filterSize { get; set; }

        public GeoArea area { get; set; }
        
        public ImageStats(GeoArea geoArea, int imageWidth, int imageHeight)
        {
            //Pluscode parameters
            imageSizeX = imageWidth;
            imageSizeY = imageHeight;

            area = geoArea;
            degreesPerPixelX = area.LongitudeWidth / imageSizeX;
            degreesPerPixelY = area.LatitudeHeight / imageSizeY;
        }

        public ImageStats(int zoomLevel, int xTile, int yTile, int imageWidth, int imageHeight)
        {
            //Slippy map parameters
            var n = Math.Pow(2, zoomLevel);

            var lon_degree_w = xTile / n * 360 - 180;
            var lon_degree_e = (xTile + 1) / n * 360 - 180;

            var lat_rads_n = Math.Atan(Math.Sinh(Math.PI * (1 - 2 * yTile / n)));
            var lat_degree_n = lat_rads_n * 180 / Math.PI;

            var lat_rads_s = Math.Atan(Math.Sinh(Math.PI * (1 - 2 * (yTile + 1) / n)));
            var lat_degree_s = lat_rads_s * 180 / Math.PI;

            var areaHeightDegrees = lat_degree_n - lat_degree_s;
            var areaWidthDegrees = 360 / n;

            area = new GeoArea(lat_degree_s, lon_degree_w, lat_degree_n, lon_degree_e);

            imageSizeX = imageWidth;
            imageSizeY = imageHeight;

            degreesPerPixelX = areaWidthDegrees / imageWidth;
            degreesPerPixelY = areaHeightDegrees / imageHeight;
        }
    }
}
