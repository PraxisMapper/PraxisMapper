using System.Collections.Generic;
using static PraxisCore.DbTables;

namespace PraxisCore.Styles
{
    /// <summary>
    /// Style with 3 default team colors for use in team-based games. Intended to be used an an overlay. Draws the color in question by matching on a tag or data entry with a key of "team" and a value of either "red", "green", or "blue"
    /// </summary>
    public static class teamColor
    {
        public static List<StyleEntry> style = new List<StyleEntry>()
        {           
            new StyleEntry() { MatchOrder = 1, Name ="Red",  StyleSet = "teamColor",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "88FF0000", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 29 },
                    new StylePaint() { HtmlColorCode = "FF0000", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 30 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "team", Value = "red", MatchType = "equals"},
            }},
            new StyleEntry() { MatchOrder = 2, Name ="Green",   StyleSet = "teamColor",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "8800FF00", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 29 },
                    new StylePaint() { HtmlColorCode = "00FF00", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 30 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "team", Value = "green", MatchType = "equals"},
            }},
            new StyleEntry() { MatchOrder = 3, Name ="Blue",  StyleSet = "teamColor",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "880000FF", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 29 },
                    new StylePaint() { HtmlColorCode = "0000FF", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 30 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "team", Value = "blue", MatchType = "equals"},
            }},
            new StyleEntry() { MatchOrder = 4, Name ="White",  StyleSet = "teamColor",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "88ffffff", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 29 },
                    new StylePaint() { HtmlColorCode = "ffffff", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 30 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "team", Value = "white", MatchType = "equals"},
            }},
            new StyleEntry() { MatchOrder = 5, Name ="Orange",   StyleSet = "teamColor",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "88ff4f00", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 29 },
                    new StylePaint() { HtmlColorCode = "ff4f00", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 30 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "team", Value = "orange", MatchType = "equals"},
            }},
            new StyleEntry() { MatchOrder = 6, Name ="Teal",  StyleSet = "teamColor",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "8853fee3", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 29 },
                    new StylePaint() { HtmlColorCode = "53fee3", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 30 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "team", Value = "teal", MatchType = "equals"},
            }},
            new StyleEntry() { MatchOrder = 7, Name ="Yellow",  StyleSet = "teamColor",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "88fbff00", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 29 },
                    new StylePaint() { HtmlColorCode = "fbff00", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 30 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "team", Value = "yellow", MatchType = "equals"},
            }},
            new StyleEntry() { MatchOrder = 8, Name ="Pink",  StyleSet = "teamColor",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "88f8a4fc", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 29 },
                    new StylePaint() { HtmlColorCode = "f8a4fc", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 30 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "team", Value = "pink", MatchType = "equals"},
            }},
            new StyleEntry() { MatchOrder = 9, Name ="outline",  StyleSet = "teamColor", //May be used more often explicitly assigned.
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "000000", FillOrStroke = "stroke", LineWidthDegrees=0, FixedWidth=5, LinePattern= "solid", LayerId = 29 },
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "team", Value = "outline", MatchType = "equals"}, 
            }},

            //background is a mandatory style entry name, but its transparent here..
            new StyleEntry() { MatchOrder = 10000, Name ="background",  StyleSet = "teamColor",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "00000000", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 101 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "*", Value = "*", MatchType = "default"},
            }},
        };
    }
}
