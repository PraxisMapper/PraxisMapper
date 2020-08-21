using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Google.Common.Geometry
{
    /**
 * This class specifies the details of how the cube faces are projected onto the
 * unit sphere. This includes getting the face ordering and orientation correct
 * so that sequentially increasing cell ids follow a continuous space-filling
 * curve over the entire sphere, and defining the transformation from cell-space
 * to cube-space (see s2.h) in order to make the cells more uniform in size.
 *
 *
 *  We have implemented three different projections from cell-space (s,t) to
 * cube-space (u,v): linear, quadratic, and tangent. They have the following
 * tradeoffs:
 *
 *  Linear - This is the fastest transformation, but also produces the least
 * uniform cell sizes. Cell areas vary by a factor of about 5.2, with the
 * largest cells at the center of each face and the smallest cells in the
 * corners.
 *
 *  Tangent - Transforming the coordinates via atan() makes the cell sizes more
 * uniform. The areas vary by a maximum ratio of 1.4 as opposed to a maximum
 * ratio of 5.2. However, each call to atan() is about as expensive as all of
 * the other calculations combined when converting from points to cell ids, i.e.
 * it reduces performance by a factor of 3.
 *
 *  Quadratic - This is an approximation of the tangent projection that is much
 * faster and produces cells that are almost as uniform in size. It is about 3
 * times faster than the tangent projection for converting cell ids to points,
 * and 2 times faster for converting points to cell ids. Cell areas vary by a
 * maximum ratio of about 2.1.
 *
 *  Here is a table comparing the cell uniformity using each projection. "Area
 * ratio" is the maximum ratio over all subdivision levels of the largest cell
 * area to the smallest cell area at that level, "edge ratio" is the maximum
 * ratio of the longest edge of any cell to the shortest edge of any cell at the
 * same level, and "diag ratio" is the ratio of the longest diagonal of any cell
 * to the shortest diagonal of any cell at the same level. "ToPoint" and
 * "FromPoint" are the times in microseconds required to convert cell ids to and
 * from points (unit vectors) respectively.
 *
 *  Area Edge Diag ToPoint FromPoint Ratio Ratio Ratio (microseconds)
 * ------------------------------------------------------- 
 * Linear: 5.200 2.117 2.959 0.103 0.123 
 * Tangent: 1.414 1.414 1.704 0.290 0.306 
 * Quadratic: 2.082 1.802 1.932 0.116 0.161
 *
 *  The worst-case cell aspect ratios are about the same with all three
 * projections. The maximum ratio of the longest edge to the shortest edge
 * within the same cell is about 1.4 and the maximum ratio of the diagonals
 * within the same cell is about 1.7.
 *
 * This data was produced using s2cell_unittest and s2cellid_unittest.
 *
 */

    public static class S2Projections
    {
        private const Projections Projection = Projections.Quadratic;

        // All of the values below were obtained by a combination of hand analysis and
        // Mathematica. In general, S2_TAN_PROJECTION produces the most uniform
        // shapes and sizes of cells, S2_LINEAR_PROJECTION is considerably worse, and
        // S2_QUADRATIC_PROJECTION is somewhere in between (but generally closer to
        // the tangent projection than the linear one).


        // The minimum area of any cell at level k is at least MIN_AREA.GetValue(k),
        // and the maximum is at most MAX_AREA.GetValue(k). The average area of all
        // cells at level k is exactly AVG_AREA.GetValue(k).
        public static readonly S2CellMetric MinArea = new S2CellMetric(2,
                                                         Projection == Projections.Linear ? 1 / (3 * Math.Sqrt(3)) : // 0.192
                                                             Projection == Projections.Tan ? (S2.Pi * S2.Pi)
                                                                                                              / (16 * S2.Sqrt2) : // 0.436
                                                                 Projection == Projections.Quadratic
                                                                     ? 2 * S2.Sqrt2 / 9 : // 0.314
                                                                     0);

        public static readonly S2CellMetric MaxArea = new S2CellMetric(2,
                                                         Projection == Projections.Linear ? 1 : // 1.000
                                                             Projection == Projections.Tan ? S2.Pi * S2.Pi / 16 : // 0.617
                                                                 Projection == Projections.Quadratic
                                                                     ? 0.65894981424079037 : // 0.659
                                                                     0);

        public static readonly S2CellMetric AvgArea = new S2CellMetric(2, S2.Pi / 6); // 0.524)


        // Each cell is bounded by four planes passing through its four edges and
        // the center of the sphere. These metrics relate to the angle between each
        // pair of opposite bounding planes, or equivalently, between the planes
        // corresponding to two different s-values or two different t-values. For
        // example, the maximum angle between opposite bounding planes for a cell at
        // level k is MAX_ANGLE_SPAN.GetValue(k), and the average angle span for all
        // cells at level k is approximately AVG_ANGLE_SPAN.GetValue(k).
        public static readonly S2CellMetric MinAngleSpan = new S2CellMetric(1,
                                                               Projection == Projections.Linear ? 0.5 : // 0.500
                                                                   Projection == Projections.Tan ? S2.Pi / 4 : // 0.785
                                                                       Projection == Projections.Quadratic ? 2.0 / 3 : // 0.667
                                                                           0);

        public static readonly S2CellMetric MaxAngleSpan = new S2CellMetric(1,
                                                               Projection == Projections.Linear ? 1 : // 1.000
                                                                   Projection == Projections.Tan ? S2.Pi / 4 : // 0.785
                                                                       Projection == Projections.Quadratic
                                                                           ? 0.85244858959960922 : // 0.852
                                                                           0);

        public static readonly S2CellMetric AvgAngleSpan = new S2CellMetric(1, S2.Pi / 4); // 0.785


        // The width of geometric figure is defined as the distance between two
        // parallel bounding lines in a given direction. For cells, the minimum
        // width is always attained between two opposite edges, and the maximum
        // width is attained between two opposite vertices. However, for our
        // purposes we redefine the width of a cell as the perpendicular distance
        // between a pair of opposite edges. A cell therefore has two widths, one
        // in each direction. The minimum width according to this definition agrees
        // with the classic geometric one, but the maximum width is different. (The
        // maximum geometric width corresponds to MAX_DIAG defined below.)
        //
        // For a cell at level k, the distance between opposite edges is at least
        // MIN_WIDTH.GetValue(k) and at most MAX_WIDTH.GetValue(k). The average
        // width in both directions for all cells at level k is approximately
        // AVG_WIDTH.GetValue(k).
        //
        // The width is useful for bounding the minimum or maximum distance from a
        // point on one edge of a cell to the closest point on the opposite edge.
        // For example, this is useful when "growing" regions by a fixed distance.
        public static readonly S2CellMetric MinWidth = new S2CellMetric(1,
                                                          (Projection == Projections.Linear ? 1 / Math.Sqrt(6) : // 0.408
                                                               Projection == Projections.Tan ? S2.Pi / (4 * S2.Sqrt2) : // 0.555
                                                                   Projection == Projections.Quadratic ? S2.Sqrt2 / 3 : // 0.471
                                                                       0));

        public static readonly S2CellMetric MaxWidth = new S2CellMetric(1, MaxAngleSpan.Deriv());

        public static readonly S2CellMetric AvgWidth = new S2CellMetric(1,
                                                          Projection == Projections.Linear ? 0.70572967292222848 : // 0.706
                                                              Projection == Projections.Tan ? 0.71865931946258044 : // 0.719
                                                                  Projection == Projections.Quadratic
                                                                      ? 0.71726183644304969 : // 0.717
                                                                      0);

        // The minimum edge length of any cell at level k is at least
        // MIN_EDGE.GetValue(k), and the maximum is at most MAX_EDGE.GetValue(k).
        // The average edge length is approximately AVG_EDGE.GetValue(k).
        //
        // The edge length metrics can also be used to bound the minimum, maximum,
        // or average distance from the center of one cell to the center of one of
        // its edge neighbors. In particular, it can be used to bound the distance
        // between adjacent cell centers along the space-filling Hilbert curve for
        // cells at any given level.
        public static readonly S2CellMetric MinEdge = new S2CellMetric(1,
                                                         Projection == Projections.Linear ? S2.Sqrt2 / 3 : // 0.471
                                                             Projection == Projections.Tan ? S2.Pi / (4 * S2.Sqrt2) : // 0.555
                                                                 Projection == Projections.Quadratic ? S2.Sqrt2 / 3 : // 0.471
                                                                     0);

        public static readonly S2CellMetric MaxEdge = new S2CellMetric(1, MaxAngleSpan.Deriv());

        public static readonly S2CellMetric AvgEdge = new S2CellMetric(1,
                                                         Projection == Projections.Linear ? 0.72001709647780182 : // 0.720
                                                             Projection == Projections.Tan ? 0.73083351627336963 : // 0.731
                                                                 Projection == Projections.Quadratic
                                                                     ? 0.72960687319305303 : // 0.730
                                                                     0);


        // The minimum diagonal length of any cell at level k is at least
        // MIN_DIAG.GetValue(k), and the maximum is at most MAX_DIAG.GetValue(k).
        // The average diagonal length is approximately AVG_DIAG.GetValue(k).
        //
        // The maximum diagonal also happens to be the maximum diameter of any cell,
        // and also the maximum geometric width (see the discussion above). So for
        // example, the distance from an arbitrary point to the closest cell center
        // at a given level is at most half the maximum diagonal length.
        public static readonly S2CellMetric MinDiag = new S2CellMetric(1,
                                                         Projection == Projections.Linear ? S2.Sqrt2 / 3 : // 0.471
                                                             Projection == Projections.Tan ? S2.Pi / (3 * S2.Sqrt2) : // 0.740
                                                                 Projection == Projections.Quadratic
                                                                     ? 4 * S2.Sqrt2 / 9 : // 0.629
                                                                     0);

        public static readonly S2CellMetric MaxDiag = new S2CellMetric(1,
                                                         Projection == Projections.Linear ? S2.Sqrt2 : // 1.414
                                                             Projection == Projections.Tan ? S2.Pi / Math.Sqrt(6) : // 1.283
                                                                 Projection == Projections.Quadratic
                                                                     ? 1.2193272972170106 : // 1.219
                                                                     0);

        public static readonly S2CellMetric AvgDiag = new S2CellMetric(1,
                                                         Projection == Projections.Linear ? 1.0159089332094063 : // 1.016
                                                             Projection == Projections.Tan ? 1.0318115985978178 : // 1.032
                                                                 Projection == Projections.Quadratic
                                                                     ? 1.03021136949923584 : // 1.030
                                                                     0);

        // This is the maximum edge aspect ratio over all cells at any level, where
        // the edge aspect ratio of a cell is defined as the ratio of its longest
        // edge length to its shortest edge length.
        public static readonly double MaxEdgeAspect =
            Projection == Projections.Linear ? S2.Sqrt2 : // 1.414
                Projection == Projections.Tan ? S2.Sqrt2 : // 1.414
                    Projection == Projections.Quadratic ? 1.44261527445268292 : // 1.443
                        0;

        // This is the maximum diagonal aspect ratio over all cells at any level,
        // where the diagonal aspect ratio of a cell is defined as the ratio of its
        // longest diagonal length to its shortest diagonal length.
        public static readonly double MaxDiagAspect = Math.Sqrt(3); // 1.732

        public static double StToUv(double s)
        {
            switch (Projection)
            {
                case Projections.Linear:
                    return s;
                case Projections.Tan:
                    // Unfortunately, tan(M_PI_4) is slightly less than 1.0. This isn't due
                    // to
                    // a flaw in the implementation of tan(), it's because the derivative of
                    // tan(x) at x=pi/4 is 2, and it happens that the two adjacent floating
                    // point numbers on either side of the infinite-precision value of pi/4
                    // have
                    // tangents that are slightly below and slightly above 1.0 when rounded
                    // to
                    // the nearest double-precision result.
                    s = Math.Tan(S2.PiOver4 * s);
                    return s + (1.0 / (1L << 53)) * s;
                case Projections.Quadratic:
                    if (s >= 0)
                    {
                        return (1 / 3.0) * ((1 + s) * (1 + s) - 1);
                    }
                    else
                    {
                        return (1 / 3.0) * (1 - (1 - s) * (1 - s));
                    }
                default:
                    throw new ArgumentOutOfRangeException("Invalid value for S2_PROJECTION");
            }
        }

        public static double UvToSt(double u)
        {
            switch (Projection)
            {
                case Projections.Linear:
                    return u;
                case Projections.Tan:
                    return (4 * S2.InversePi) * Math.Atan(u);
                case Projections.Quadratic:
                    if (u >= 0)
                    {
                        return Math.Sqrt(1 + 3 * u) - 1;
                    }
                    else
                    {
                        return 1 - Math.Sqrt(1 - 3 * u);
                    }
                default:
                    throw new ArgumentOutOfRangeException("Invalid value for S2_PROJECTION");
            }
        }


        /**
   * Convert (face, u, v) coordinates to a direction vector (not necessarily
   * unit length).
   */

        public static S2Point FaceUvToXyz(int face, double u, double v)
        {
            switch (face)
            {
                case 0:
                    return new S2Point(1, u, v);
                case 1:
                    return new S2Point(-u, 1, v);
                case 2:
                    return new S2Point(-u, -v, 1);
                case 3:
                    return new S2Point(-1, -v, -u);
                case 4:
                    return new S2Point(v, -1, -u);
                default:
                    return new S2Point(v, u, -1);
            }
        }

        public static R2Vector ValidFaceXyzToUv(int face, S2Point p)
        {
            // assert (p.dotProd(faceUvToXyz(face, 0, 0)) > 0);
            double pu;
            double pv;
            switch (face)
            {
                case 0:
                    pu = p.Y / p.X;
                    pv = p.Z / p.X;
                    break;
                case 1:
                    pu = -p.X / p.Y;
                    pv = p.Z / p.Y;
                    break;
                case 2:
                    pu = -p.X / p.Z;
                    pv = -p.Y / p.Z;
                    break;
                case 3:
                    pu = p.Z / p.X;
                    pv = p.Y / p.X;
                    break;
                case 4:
                    pu = p.Z / p.Y;
                    pv = -p.X / p.Y;
                    break;
                default:
                    pu = -p.Y / p.Z;
                    pv = -p.X / p.Z;
                    break;
            }
            return new R2Vector(pu, pv);
        }

        public static int XyzToFace(S2Point p)
        {
            var face = p.LargestAbsComponent;
            if (p[face] < 0)
            {
                face += 3;
            }
            return face;
        }

        public static R2Vector? FaceXyzToUv(int face, S2Point p)
        {
            if (face < 3)
            {
                if (p[face] <= 0)
                {
                    return null;
                }
            }
            else
            {
                if (p[face - 3] >= 0)
                {
                    return null;
                }
            }
            return ValidFaceXyzToUv(face, p);
        }

        public static S2Point GetUNorm(int face, double u)
        {
            switch (face)
            {
                case 0:
                    return new S2Point(u, -1, 0);
                case 1:
                    return new S2Point(1, u, 0);
                case 2:
                    return new S2Point(1, 0, u);
                case 3:
                    return new S2Point(-u, 0, 1);
                case 4:
                    return new S2Point(0, -u, 1);
                default:
                    return new S2Point(0, -1, -u);
            }
        }

        public static S2Point GetVNorm(int face, double v)
        {
            switch (face)
            {
                case 0:
                    return new S2Point(-v, 0, 1);
                case 1:
                    return new S2Point(0, -v, 1);
                case 2:
                    return new S2Point(0, -1, -v);
                case 3:
                    return new S2Point(v, -1, 0);
                case 4:
                    return new S2Point(1, v, 0);
                default:
                    return new S2Point(1, 0, v);
            }
        }

        public static S2Point GetNorm(int face)
        {
            return FaceUvToXyz(face, 0, 0);
        }

        public static S2Point GetUAxis(int face)
        {
            switch (face)
            {
                case 0:
                    return new S2Point(0, 1, 0);
                case 1:
                    return new S2Point(-1, 0, 0);
                case 2:
                    return new S2Point(-1, 0, 0);
                case 3:
                    return new S2Point(0, 0, -1);
                case 4:
                    return new S2Point(0, 0, -1);
                default:
                    return new S2Point(0, 1, 0);
            }
        }

        public static S2Point GetVAxis(int face)
        {
            switch (face)
            {
                case 0:
                    return new S2Point(0, 0, 1);
                case 1:
                    return new S2Point(0, 0, 1);
                case 2:
                    return new S2Point(0, -1, 0);
                case 3:
                    return new S2Point(0, -1, 0);
                case 4:
                    return new S2Point(1, 0, 0);
                default:
                    return new S2Point(1, 0, 0);
            }
        }
    }

    public enum Projections
    {
        Linear,
        Tan,
        Quadratic
    }
}