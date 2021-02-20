using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SixLabors.ImageSharp.Drawing;

namespace CoreComponents.Support
{
    class DrawingRules
    {
        //Hold the stuff for drawing MapTiles, in the event that you want different rules on different map tiles.
        //Would want:
        //Name
        //list<Brush> for each area type to draw it as.
        //maybe a rules processor that loads them from a database instead of being fixed in code?
        public void stuff()
        {
            string name;
            var a = new SixLabors.ImageSharp.Drawing.Processing.SolidBrush(new SixLabors.ImageSharp.Color(new SixLabors.ImageSharp.PixelFormats.Rgba32(255, 255, 255)));
            
        }
        
    }
}
