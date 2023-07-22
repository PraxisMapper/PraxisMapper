using System.Collections.Generic;
using static PraxisCore.DbTables;

namespace PraxisCore.Styles
{
    /// <summary>
    /// An abbreviated, bolder-colored version of the default mapTiles style, that only draws elements likely to be good places for gameplay.
    /// </summary>
    public static class namedBuilding
    {
        public static List<StyleEntry> style = new List<StyleEntry>()
        {
            new StyleEntry() { MatchOrder = 69, Name ="namedBuilding", StyleSet = "namedBuilding", IsGameElement = true,
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "d1b6a1", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 },
                    new StylePaint() { HtmlColorCode = "806b5b", FillOrStroke = "stroke", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 99 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "building", Value = "*", MatchType = "any" },
                    new StyleMatchRule() { Key = "name", Value = "*", MatchType = "any" }
                }
            },
            //background is a mandatory style entry name, but its transparent here..
            new StyleEntry() { MatchOrder = 10000, Name ="background",  StyleSet = "namedBuilding",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "FFFFFF", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 101 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "bg", Value = "bg", MatchType = "equals"}, //this one only gets called by name anyways.
            }},
            //this name needs to exist because of the default style using this name.
            new StyleEntry() { MatchOrder = 10001, Name ="unmatched",  StyleSet = "namedBuilding",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "00000000", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "a", Value = "s", MatchType = "equals" }}
            },
        };
    }
}
