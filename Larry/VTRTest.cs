using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VectorTileRendererPraxisMapper;

namespace Larry
{
    public static class VTRTest
    {
        public static void DrawTileFromPBF(string filename)
        {
            // load style and fonts
            var style = new Style("liberty-style.json");
            style.FontDirectory = "styles/fonts/";

            // set pbf as tile provider
            var provider = new VectorTileRendererPraxisMapper.Sources.PbfTileSource(filename); //This appears to want a MapBox format file, not an OSM format file. SO I need to change that up before this work the way I want it to.
            //style.SetSourceProvider(0, provider);

            // render it on a skia canvas
            var zoom = 13; //might want to zoom in closer?
            var canvas = new SkiaCanvas();
            //Delaware is 2384, 3138,13
            //var bitmap = Renderer.Render(style, canvas, 2384, 3138, zoom, 512, 512, 1).Result;

            //System.IO.File.WriteAllBytes("testTile.png", bitmap);


            //imageView.Source = bitmap;
        }
    }
}
