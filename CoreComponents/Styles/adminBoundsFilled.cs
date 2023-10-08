using System.Collections.Generic;
using static PraxisCore.DbTables;

namespace PraxisCore.Styles
{
    /// <summary>
    /// Only draws administrative boundaries, of all sizes. Names follow typical rules for the USA. Transparent background, so these can be used as overlays.
    /// </summary>
    public static class adminBoundsFilled
    {
        //adminBounds: Draws only admin boundaries, all levels supported but names match USA's common usage of them
        public static List<StyleEntry> style = new List<StyleEntry>()
        {
            new StyleEntry() { MatchOrder = 1, Name ="country",  StyleSet = "adminBoundsFilled",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "E31010", FillOrStroke = "fill", StaticColorFromName = true, LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "boundary", Value = "administrative", MatchType = "equals" },
                    new StyleMatchRule() { Key = "admin_level", Value = "2", MatchType = "equals" }
                }
            },
            new StyleEntry() { MatchOrder = 2, Name ="region",  StyleSet = "adminBoundsFilled",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "CC8A58", FillOrStroke = "fill", StaticColorFromName = true, LayerId = 90 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "boundary", Value = "administrative", MatchType = "equals" },
                    new StyleMatchRule() { Key = "admin_level", Value = "3", MatchType = "equals" }
                }
            },
            new StyleEntry() { MatchOrder = 3, Name ="state",   StyleSet = "adminBoundsFilled",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "FFE30D", FillOrStroke = "fill", StaticColorFromName = true, LayerId = 80 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "boundary", Value = "administrative", MatchType = "equals" },
                    new StyleMatchRule() { Key = "admin_level", Value = "4", MatchType = "equals" }
                }
            },
            new StyleEntry() { MatchOrder = 4, Name ="admin5", StyleSet = "adminBoundsFilled",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "369670", FillOrStroke = "fill", StaticColorFromName = true, LayerId = 70 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "boundary", Value = "administrative", MatchType = "equals" },
                    new StyleMatchRule() { Key = "admin_level", Value = "5", MatchType = "equals" }
                }
            },
            new StyleEntry() { MatchOrder = 5, Name ="county",  StyleSet = "adminBoundsFilled",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "3E8A25", FillOrStroke = "fill", StaticColorFromName = true, LayerId = 60 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "boundary", Value = "administrative", MatchType = "equals" },
                    new StyleMatchRule() { Key = "admin_level", Value = "6", MatchType = "equals" }
                }
            },
            new StyleEntry() { MatchOrder = 6, Name ="township",  StyleSet = "adminBoundsFilled",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "32FCF6", FillOrStroke = "fill", StaticColorFromName = true, LayerId = 50 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "boundary", Value = "administrative", MatchType = "equals" },
                    new StyleMatchRule() { Key = "admin_level", Value = "7", MatchType = "equals" }
                }
            },
            new StyleEntry() { MatchOrder = 7, Name ="city",  StyleSet = "adminBoundsFilled",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "0F34BA", FillOrStroke = "fill", StaticColorFromName = true, LayerId = 40 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "boundary", Value = "administrative", MatchType = "equals" },
                    new StyleMatchRule() { Key = "admin_level", Value = "8", MatchType = "equals" }
                }
            },
            new StyleEntry() { MatchOrder = 8, Name ="ward",  StyleSet = "adminBoundsFilled",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "A46DFC", FillOrStroke = "fill", StaticColorFromName = true, LayerId = 30 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "boundary", Value = "administrative", MatchType = "equals" },
                    new StyleMatchRule() { Key = "admin_level", Value = "9", MatchType = "equals" }
                }
            },
            new StyleEntry() { MatchOrder = 9, Name ="neighborhood",  StyleSet = "adminBoundsFilled",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "B811B5", FillOrStroke = "fill", StaticColorFromName = true, LayerId = 20 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "boundary", Value = "administrative", MatchType = "equals" },
                    new StyleMatchRule() { Key = "admin_level", Value = "10", MatchType = "equals" }
                }
            },
            new StyleEntry() { MatchOrder = 10, Name ="admin11", StyleSet = "adminBoundsFilled",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "00FF2020", FillOrStroke = "fill", LayerId = 10 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "boundary", Value = "administrative", MatchType = "equals" },
                    new StyleMatchRule() { Key = "admin_level", Value = "11", MatchType = "equals" }
                }
            },
            new StyleEntry() { MatchOrder = 9999, Name ="background",  StyleSet = "adminBoundsFilled",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "00F2EFE9", FillOrStroke = "fill", LayerId = 101 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "*", Value = "*", MatchType = "none" }} },

            new StyleEntry() { MatchOrder = 10000, Name ="unmatched",  StyleSet = "adminBoundsFilled",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "00000000", FillOrStroke = "fill", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "*", Value = "*", MatchType = "default" }}
            },
        };
    }
}
