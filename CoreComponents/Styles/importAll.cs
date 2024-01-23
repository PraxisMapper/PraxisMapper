using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static PraxisCore.DbTables;

namespace PraxisCore.Styles
{
    public static class importAll
    {
        //This style uses a special rule that accepts all entries with a tag. This lets us load everything in one pass from a pbf file.
        public static List<StyleEntry> style = new List<StyleEntry>()
        {
            new StyleEntry() { MatchOrder = 1, Name ="1",  StyleSet = "importAll",
                PaintOperations = new List<StylePaint>() { //Making linewidthdegrees = 0 means it will always draw as 1 px wide, at least with SkiaSharp
                    new StylePaint() { HtmlColorCode = "000000", FillOrStroke = "stroke", LineWidthDegrees=0, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "*", Value = "*", MatchType = "tagged"},
            }},
            //background is a mandatory style entry name, but its transparent here.
            new StyleEntry() { MatchOrder = 10000, Name ="background",  StyleSet = "importAll",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "00000000", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 101 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "bg", Value = "bg", MatchType = "equals"},
            }},
            //this name needs to exist because of the default style using this name.
            new StyleEntry() { MatchOrder = 10001, Name ="unmatched",  StyleSet = "importAll",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "00000000", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "a", Value = "s", MatchType = "equals" }}
            },
        };
    }
}
