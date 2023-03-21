using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using NetTopologySuite.Geometries;
using PraxisCore;
using PraxisMapper.Classes;

namespace PraxisDemosPlugin.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class SplatterController : Controller
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
        [Route("[controller]/Splat/{plusCode}/{radius}")]
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

        [HttpPut]
        [Route("[controller]/Enter")]
        public void Enter()
        {
            //A user has entered a space, grant them a point to use to Splat later.

        }

        private Geometry MakeSplatShapeSimple(Point p, double radius)
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

        private Geometry MakeSplatShape(Point p, double radius)
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
                var randomMoveX = ((Random.Shared.Next() % 2 == 0 ? 1 : -1) * radius * (Random.Shared.Next(50, 75)) / 100);
                var randomMoveY = ((Random.Shared.Next() % 2 == 0 ? 1 : -1) * radius * (Random.Shared.Next(50, 75)) / 100);
                workPoint = new Point(p.X + randomMoveX, p.Y + randomMoveY);
                var bulgeGeo = workPoint.Buffer(radius / 3);
                geometries.Add(bulgeGeo);
            }

            //STep 3: add some distant circles, and connect them to the center with a triangle.
            randCount = Random.Shared.Next(3, 6);
            for (int i = 0; i < randCount; i++)
            {
                var randomMoveX = ((Random.Shared.Next() % 2 == 0 ? 1 : -1) * radius * (Random.Shared.Next(75, 100)) / 100);
                var randomMoveY = ((Random.Shared.Next() % 2 == 0 ? 1 : -1) * radius * (Random.Shared.Next(75, 100)) / 100);
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

                var connector = Singletons.geometryFactory.CreatePolygon(new Coordinate[] { new Coordinate(outerCircle.Centroid.X, outerCircle.Centroid.Y), new Coordinate(t1.X, t1.Y), new Coordinate(t2.X, t2.Y), new Coordinate(outerCircle.Centroid.X, outerCircle.Centroid.Y) });
                geometries.Add(connector); 
            }

            var resultGeo = Singletons.reducer.Reduce(NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(geometries).Simplify(ConstantValues.resolutionCell11Lon));
            return resultGeo;
        }
    }
}