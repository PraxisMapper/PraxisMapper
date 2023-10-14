using System.Collections.Generic;
using static PraxisCore.DbTables;

namespace PraxisCore.Styles
{
    /// <summary>
    /// Only draws administrative boundaries, of all sizes. Names follow typical rules for the USA. Transparent background, so these can be used as overlays.
    /// </summary>
    public static class adminBounds
    {
        //adminBounds: Draws only admin boundaries, all levels supported but names match USA's common usage of them
        public static List<StyleEntry> style = new List<StyleEntry>()
        {
            new StyleEntry() { MatchOrder = 1, Name ="country",  StyleSet = "adminBounds",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "E31010", FillOrStroke = "stroke", LineWidthDegrees = 0.00005f, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "boundary", Value = "administrative", MatchType = "equals" },
                    new StyleMatchRule() { Key = "admin_level", Value = "2", MatchType = "equals" },
                    new StyleMatchRule() { Key = "ISO3166-1", Value = "*", MatchType = "any" }
                }
            },
            new StyleEntry() { MatchOrder = 2, Name ="region",  StyleSet = "adminBounds", //dot pattern
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "CC8A58", FillOrStroke = "stroke", LineWidthDegrees = 0.00004f, LinePattern= "10|10", LayerId = 90 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "boundary", Value = "administrative", MatchType = "equals" },
                    new StyleMatchRule() { Key = "admin_level", Value = "3", MatchType = "equals" }
                }
            },
            new StyleEntry() { MatchOrder = 3, Name ="state",   StyleSet = "adminBounds", //dot pattern
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "FFE30D", FillOrStroke = "stroke", LineWidthDegrees = 0.00003f, LinePattern= "10|5", LayerId = 80 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "boundary", Value = "administrative", MatchType = "equals" },
                    new StyleMatchRule() { Key = "admin_level", Value = "4", MatchType = "equals" }
                }
            },
            new StyleEntry() { MatchOrder = 4, Name ="admin5", StyleSet = "adminBounds", //Line pattern is dash-dot-dot
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "369670", FillOrStroke = "stroke", LineWidthDegrees = 0.00002f, LinePattern= "20|10|10|10|10|10", LayerId = 70 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "boundary", Value = "administrative", MatchType = "equals" },
                    new StyleMatchRule() { Key = "admin_level", Value = "5", MatchType = "equals" }
                }
            },
            new StyleEntry() { MatchOrder = 5, Name ="county",  StyleSet = "adminBounds", //dash-dot pattern
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "3E8A25", FillOrStroke = "stroke", LineWidthDegrees = 0.00001f, LinePattern= "20|10|10|10", LayerId = 60 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "boundary", Value = "administrative", MatchType = "equals" },
                    new StyleMatchRule() { Key = "admin_level", Value = "6", MatchType = "equals" }
                }
            },
            new StyleEntry() { MatchOrder = 6, Name ="township",  StyleSet = "adminBounds", //dash
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "32FCF6", FillOrStroke = "stroke", LineWidthDegrees = 0.000009f, LinePattern= "25|10", LayerId = 50 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "boundary", Value = "administrative", MatchType = "equals" },
                    new StyleMatchRule() { Key = "admin_level", Value = "7", MatchType = "equals" }
                }
            },
            new StyleEntry() { MatchOrder = 7, Name ="city",  StyleSet = "adminBounds", //dash
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "0F34BA", FillOrStroke = "stroke", LineWidthDegrees = 0.000007f, LinePattern= "20|10", LayerId = 40 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "boundary", Value = "administrative", MatchType = "equals" },
                    new StyleMatchRule() { Key = "admin_level", Value = "8", MatchType = "equals" }
                }
            },
            new StyleEntry() { MatchOrder = 8, Name ="ward",  StyleSet = "adminBounds", //dot
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "A46DFC", FillOrStroke = "stroke", LineWidthDegrees = 0.000005f, LinePattern= "10|10", LayerId = 30 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "boundary", Value = "administrative", MatchType = "equals" },
                    new StyleMatchRule() { Key = "admin_level", Value = "9", MatchType = "equals" }
                }
            },
            new StyleEntry() { MatchOrder = 9, Name ="neighborhood",  StyleSet = "adminBounds", //dot
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "B811B5", FillOrStroke = "stroke", LineWidthDegrees = 0.000003f, LinePattern= "10|5", LayerId = 20 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "boundary", Value = "administrative", MatchType = "equals" },
                    new StyleMatchRule() { Key = "admin_level", Value = "10", MatchType = "equals" }
                }
            },
            new StyleEntry() { MatchOrder = 10, Name ="admin11", StyleSet = "adminBounds", //not rendered
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "00FF2020", FillOrStroke = "stroke", LineWidthDegrees = 0.000001f, LinePattern= "solid", LayerId = 10 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "boundary", Value = "administrative", MatchType = "equals" },
                    new StyleMatchRule() { Key = "admin_level", Value = "11", MatchType = "equals" }
                }
            },
            new StyleEntry() { MatchOrder = 9999, Name ="background",  StyleSet = "adminBounds",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "00F2EFE9", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 101 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "*", Value = "*", MatchType = "none" }} },

            new StyleEntry() { MatchOrder = 10000, Name ="unmatched",  StyleSet = "adminBounds",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "00000000", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "*", Value = "*", MatchType = "default" }}
            },
        };
    }
}
