using Google.OpenLocationCode;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Prepared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CoreComponents
{
    public static class BoundsCheck
    {
        public static bool IsInBounds(IPreparedGeometry bounds, GeoArea place)
        {
            if (bounds.Intersects(Converters.GeoAreaToPolygon(place)))
                return true;

            return false;
        }

        public static bool IsInBounds(IPreparedGeometry bounds, Polygon place)
        {
            if (bounds.Intersects(place))
                return true;

            return false;
        }

        public static bool IsInBounds(IPreparedGeometry bounds, double lat, double lon)
        {
            Point p = new Point(lon, lat);
            if (bounds.Intersects(p))
                return true;

            return false;
        }
    }
}
