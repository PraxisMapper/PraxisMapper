using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using NetTopologySuite.Geometries;
using PraxisCore;
using PraxisCore.Support;
using PraxisMapper.Classes;

namespace PraxisDemosPlugin.Controllers
{
    [ApiController]
    [Route("/[controller]")]
    public class SplatterController : Controller, IPraxisPlugin
    {
        string accountId, password;

        //set of colors to randomly pick
        static List<string> htmlColors = new List<string>() { "#99ff0000", "#9900ff00", "#990000ff" }; 
        static Dictionary<string, Geometry> splatCollection = new Dictionary<string, Geometry>();


        public override void OnActionExecuting(ActionExecutingContext context)
        {
            base.OnActionExecuting(context);
            PraxisAuthentication.GetAuthInfo(Response, out accountId, out password);
        }

        //TODO: Initialize this state on a Startup() call.

        [HttpPut]
        [Route("/[controller]/Splat/{plusCode}/{radius}")]
        public void Splat(string plusCode, double radius)
        {
            //A user wants to throw down a paint mark in the center of {plusCode} with a size of {radius} (in degrees)
            var newGeo = MakeSplatShape(plusCode.ToGeoArea().ToPoint(), radius);
            var color = htmlColors.PickOneRandom();

            foreach (var s in splatCollection)
            {
                Geometry temp = s.Value;
                if (s.Key == color)
                    temp = s.Value.Union(newGeo);
                else
                    temp = s.Value.Difference(newGeo);
                splatCollection[s.Key] = temp;
            }

            //TODO: save to DB
        }

        [HttpGet]
        [Route("/[controller]/Enter")]
        public void Enter()
        {
            //A user has entered a space, grant them a point to use to Splat later.

        }
        
        [HttpGet]
        [Route("/[controller]/Test/{plusCode8}")]
        public ActionResult TestShapes(string plusCode8)
        {
            byte[] results = null;
            var mapTile1 = MapTileSupport.DrawPlusCode(plusCode8);

            var possiblePoints = plusCode8.GetSubCells();

            List<Geometry> points = new List<Geometry>(3);
            for (int i = 0; i < 3; i++)
                points.Add(Singletons.geometryFactory.CreatePolygon());

            List<DbTables.Place> places = new List<DbTables.Place>();
            List<string> colors = new List<string>() { "1", "2", "3" };

            for(int i = 0; i < 24; i++)
            {
                var thisPoint = possiblePoints.PickOneRandom();
                var color = colors.PickOneRandom().ToInt();
                possiblePoints.Remove(thisPoint);
                var splat = MakeSplatShape(thisPoint.ToGeoArea().ToPoint(), Random.Shared.Next(2, 7) * .0001);
                //var place = new DbTables.Place() { DrawSizeHint = 1, ElementGeometry = splat, StyleName = colors.PickOneRandom() };
                for (int j = 0; j < 3; j++)
                {
                    if (color - 1 == j)
                        points[j] = points[j].Union(splat);
                    else
                        points[j] = points[j].Difference(splat);
                }
            }

            for(int i = 0; i < points.Count; i++)
            {
                places.Add(new DbTables.Place() { DrawSizeHint = 1, ElementGeometry = points[i], StyleName = colors[i] });
            }

            var stats = new ImageStats(plusCode8);
            var paintOps = MapTileSupport.GetPaintOpsForPlaces(places, "teamColor", stats);
            var overlay = MapTileSupport.MapTiles.DrawAreaAtSize(stats, paintOps);


            results = MapTileSupport.MapTiles.LayerTiles(stats, mapTile1, overlay);

            return File(results, "image/png");
            //return File(overlay, "image/png");

        }

        private Geometry MakeSplatShapeSimple(Point p, double radius) //~6ms average
        {
            //The lazy option for this: a few random circles.
            List<Geometry> geometries = new List<Geometry>();
            var randCount = Random.Shared.Next(5, 9);
            var bufferSize = radius / 3;
            for (int i = 0; i < randCount; i++)
            {
                var randomMoveX = ((Random.Shared.Next() % 2 == 0 ? 1 : -1) * radius * (Random.Shared.NextDouble()));
                var randomMoveY = ((Random.Shared.Next() % 2 == 0 ? 1 : -1) * radius * (Random.Shared.NextDouble()));
                var workPoint = new Point(p.X + randomMoveX, p.Y + randomMoveY);
                workPoint.Buffer(bufferSize);
                geometries.Add(workPoint);
            }

            var resultGeo = NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(geometries);
            return resultGeo;
        }

        private Geometry MakeSplatShape(Point p, double radius) //12ms average
        {
            //Do some geometry actions to make a shape to put on the map.
            List<Geometry> geometries = new List<Geometry>();

            //Step 1: fill in the middle half of the radius.
            var workPoint =  new Point(p.X, p.Y);
            var centerGeo = workPoint.Buffer(radius / 2);
            geometries.Add(centerGeo);

            //Step 2: Add some bulges around it for asymmetry
            var randCount = Random.Shared.Next(3, 8);
            for(int i =0; i < randCount; i++)
            {
                var randomMoveX = ((Random.Shared.Next() % 2 == 0 ? 1 : -1) * radius * (Random.Shared.Next(15, 35)) / 100);
                var randomMoveY = ((Random.Shared.Next() % 2 == 0 ? 1 : -1) * radius * (Random.Shared.Next(15, 35)) / 100);
                workPoint = new Point(p.X + randomMoveX, p.Y + randomMoveY);
                var bulgeGeo = workPoint.Buffer(radius / 3);
                geometries.Add(bulgeGeo);
            }

            //STep 3: add some distant circles, and connect them to the center with a triangle.
            randCount = Random.Shared.Next(3, 6);
            for (int i = 0; i < randCount; i++)
            {
                var randomMoveX = ((Random.Shared.Next() % 2 == 0 ? 1 : -1) * radius * (Random.Shared.Next(50, 100)) / 100);
                var randomMoveY = ((Random.Shared.Next() % 2 == 0 ? 1 : -1) * radius * (Random.Shared.Next(50, 100)) / 100);
                workPoint = new Point(p.X + randomMoveX, p.Y + randomMoveY);
                var outerCircle = workPoint.Buffer(radius / 5);
                geometries.Add(outerCircle);

                Point t1, t2;
                //draw triangle connecting outerCircle to center. Could use trig to grab points, this is easier math.
                if (Math.Abs(randomMoveX) > Math.Abs(randomMoveY))
                {
                    //use north/south
                    t1 = new Point(outerCircle.Centroid.X, outerCircle.EnvelopeInternal.MaxY);
                    t2 = new Point(outerCircle.Centroid.X, outerCircle.EnvelopeInternal.MinY);
                }
                else
                {
                    //use east/west
                    t1 = new Point(outerCircle.EnvelopeInternal.MaxX, outerCircle.Centroid.Y);
                    t2 = new Point(outerCircle.EnvelopeInternal.MinX, outerCircle.Centroid.Y);
                }

                var connector = Singletons.geometryFactory.CreatePolygon(new Coordinate[] { new Coordinate(centerGeo.Centroid.X, centerGeo.Centroid.Y), new Coordinate(t1.X, t1.Y), new Coordinate(t2.X, t2.Y), new Coordinate(centerGeo.Centroid.X, centerGeo.Centroid.Y) });
                geometries.Add(connector);
            }

            var resultGeo = Singletons.reducer.Reduce(NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(geometries));
                //.Simplify(ConstantValues.resolutionCell11Lon)); //Simplify makes these much squarer shapes than I like.
            return resultGeo;
        }
    }
}