using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static PraxisCore.DbTables;

namespace PraxisCore.Styles
{
    public static class adminBounds
    {
        //adminBounds: Draws only admin boundaries, all levels supported but names match USA's common usage of them
        public static List<StyleEntry> style = new List<StyleEntry>()
        {
            //TODO: these need their sizes adjusted to be wider. Right now, in degrees, the overlap is nearly invisible unless you're at zoom 20. May want to use fixed sized?
            new StyleEntry() { MatchOrder = 1, Name ="country",  StyleSet = "adminBounds",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "E31010", FillOrStroke = "stroke", LineWidthDegrees=0.000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "boundary", Value = "administrative", MatchType = "equals" },
                    new StyleMatchRule() { Key = "admin_level", Value = "2", MatchType = "equals" }
                }
            },
            new StyleEntry() { MatchOrder = 2, Name ="region",  StyleSet = "adminBounds", //dot pattern
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "CC8A58", FillOrStroke = "stroke", LineWidthDegrees=0.0001125F, LinePattern= "10|10", LayerId = 90 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "boundary", Value = "administrative", MatchType = "equals" },
                    new StyleMatchRule() { Key = "admin_level", Value = "3", MatchType = "equals" }
                }
            },
            new StyleEntry() { MatchOrder = 3, Name ="state",   StyleSet = "adminBounds", //dot pattern
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "FFE30D", FillOrStroke = "stroke", LineWidthDegrees=0.0001F, LinePattern= "10|10", LayerId = 80 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "boundary", Value = "administrative", MatchType = "equals" },
                    new StyleMatchRule() { Key = "admin_level", Value = "4", MatchType = "equals" }
                }
            },
            new StyleEntry() { MatchOrder = 4, Name ="admin5", StyleSet = "adminBounds", //Line pattern is dash-dot-dot
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "369670", FillOrStroke = "stroke", LineWidthDegrees=0.0000875F, LinePattern= "20|10|10|10|10|10", LayerId = 70 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "boundary", Value = "administrative", MatchType = "equals" },
                    new StyleMatchRule() { Key = "admin_level", Value = "5", MatchType = "equals" }
                }
            },
            new StyleEntry() { MatchOrder = 5, Name ="county",  StyleSet = "adminBounds", //dash-dot pattern
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "3E8A25", FillOrStroke = "stroke", LineWidthDegrees=0.000075F, LinePattern= "20|10|10|10", LayerId = 60 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "boundary", Value = "administrative", MatchType = "equals" },
                    new StyleMatchRule() { Key = "admin_level", Value = "6", MatchType = "equals" }
                }
            },
            new StyleEntry() { MatchOrder = 6, Name ="township",  StyleSet = "adminBounds", //dash
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "32FCF6", FillOrStroke = "stroke", LineWidthDegrees=0.0000625F, LinePattern= "20|0", LayerId = 50 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "boundary", Value = "administrative", MatchType = "equals" },
                    new StyleMatchRule() { Key = "admin_level", Value = "7", MatchType = "equals" }
                }
            },
            new StyleEntry() { MatchOrder = 7, Name ="city",  StyleSet = "adminBounds", //dash
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "0F34BA", FillOrStroke = "stroke", LineWidthDegrees=0.00005F, LinePattern= "20|0", LayerId = 40 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "boundary", Value = "administrative", MatchType = "equals" },
                    new StyleMatchRule() { Key = "admin_level", Value = "8", MatchType = "equals" }
                }
            },
            new StyleEntry() { MatchOrder = 8, Name ="ward",  StyleSet = "adminBounds", //dot
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "A46DFC", FillOrStroke = "stroke", LineWidthDegrees=0.0000475F, LinePattern= "10|10", LayerId = 30 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "boundary", Value = "administrative", MatchType = "equals" },
                    new StyleMatchRule() { Key = "admin_level", Value = "9", MatchType = "equals" }
                }
            },
            new StyleEntry() { MatchOrder = 9, Name ="neighborhood",  StyleSet = "adminBounds", //dot
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "B811B5", FillOrStroke = "stroke", LineWidthDegrees=0.000035F, LinePattern= "10|10", LayerId = 20 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "boundary", Value = "administrative", MatchType = "equals" },
                    new StyleMatchRule() { Key = "admin_level", Value = "10", MatchType = "equals" }
                }
            },
            new StyleEntry() { MatchOrder = 10, Name ="admin11", StyleSet = "adminBounds", //not rendered
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "00FF2020", FillOrStroke = "stroke", LineWidthDegrees=0.0000225F, LinePattern= "solid", LayerId = 10 }
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
