using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static PraxisCore.DbTables;

namespace PraxisCore.Styles
{
    public static class paintTown
    {
        //paintTown: A simple styleset that pulls the color to use from the tag provided
        public static List<StyleEntry> style = new List<StyleEntry>() 
        { 
            new StyleEntry() { MatchOrder = 1, Name ="tag",  StyleSet = "paintTown",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "01000000", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100, FromTag=true }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "*", Value = "*", MatchType = "default"},
            }},
            //background is a mandatory style entry name, but its transparent here.
            new StyleEntry() { MatchOrder = 10000, Name ="background",  StyleSet = "paintTown",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "00000000", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 101 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "*", Value = "*", MatchType = "default"},
            }},
        };
    }
}
