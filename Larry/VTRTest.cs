using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VectorTileRendererPraxisMapper;

namespace Larry
{
    class VTRTest
    {
        public static void DrawTileFromPBF()
        {
            // load style and fonts
            var style = new Style("basic-style.json");
            style.FontDirectory = "styles/fonts/";

            // set pbf as tile provider
            var provider = new VectorTileRendererPraxisMapper.Sources.PbfTileSource("tile.pbf"); //TODO: set this to Ohio or something.
            style.SetSourceProvider(0, provider);

            // render it on a skia canvas
            var zoom = 13; //might want to zoom in closer?
            var canvas = new SkiaCanvas();
            //TODO: check that style.Layers has a vector layer, and tweak this stuff around a little to render a tile in Ohio.
            var bitmap = Renderer.Render(style, canvas, 0, 0, zoom, 512, 512, 1).Result;

            //imageView.Source = bitmap;
        }
    }
}
