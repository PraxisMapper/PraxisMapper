using System.Collections.Generic;
using static PraxisCore.DbTables;

namespace PraxisCore.Styles
{
    /// <summary>
    /// A condensed version of suggestedGameplay, with 1 tag per element. Intended for using after Larry has been run with a command to reduce storage size.
    /// </summary>
    public static class suggestedMini
    {
        //This 
        public static List<StyleEntry> style = new List<StyleEntry>()
        {
            //SuggestedMini is set up to allow areas to be tagged with a single value to identify them. Used by the minimize process in Larry.
            new StyleEntry() { IsGameElement = true, MatchOrder = 1, Name ="park", StyleSet = "suggestedmini",
                PaintOperations = new List<StylePaint>() {

                    new StylePaint() { HtmlColorCode = "CC93FF61", FillOrStroke = "fill", LineWidthDegrees=0.0000625F, LinePattern= "solid", LayerId = 100 },
                    new StylePaint() { HtmlColorCode = "CCB2FF8F", FillOrStroke = "stroke", LineWidthDegrees=0.0001875F, LinePattern= "solid", LayerId = 99 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "suggestedmini", Value = "park", MatchType = "equals"},
            }},
            new StyleEntry() { IsGameElement = true, MatchOrder = 2, Name ="university", StyleSet = "suggestedmini",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "CCFFFFE5", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 },
                    new StylePaint() { HtmlColorCode = "CCF5EED3", FillOrStroke = "stroke", LineWidthDegrees=0.0001875F, LinePattern= "solid", LayerId = 99 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "suggestedmini", Value = "university", MatchType = "equals"} },
            },
            new StyleEntry() { IsGameElement = true, MatchOrder = 3, Name ="natureReserve", StyleSet = "suggestedmini",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "CC124504", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 },
                    new StylePaint() { HtmlColorCode = "CC027021", FillOrStroke = "stroke", LineWidthDegrees=0.0001875F, LinePattern= "solid", LayerId = 99 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "suggestedmini", Value = "natureReserve", MatchType = "equals"},
            } },
            new StyleEntry() {IsGameElement = true, MatchOrder = 4, Name ="cemetery", StyleSet = "suggestedmini",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "CCAACBAF", FillOrStroke = "fill", FileName="Landuse-cemetery.png", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 },
                    new StylePaint() { HtmlColorCode = "CC404040", FillOrStroke = "stroke", LineWidthDegrees=0.0001875F, LinePattern= "solid", LayerId = 99 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "suggestedmini", Value = "cemetery", MatchType = "equals"},
            } },
            new StyleEntry() { IsGameElement = true, MatchOrder = 5, Name ="historical", StyleSet = "suggestedmini",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "CCB3B3B3", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 },
                    new StylePaint() { HtmlColorCode = "CC9D9D9D", FillOrStroke = "stroke", LineWidthDegrees=0.0001875F, LinePattern= "solid", LayerId = 99 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "suggestedmini", Value = "historical", MatchType = "equals"} },
            },
            new StyleEntry() { IsGameElement = true, MatchOrder = 6, Name ="artsCulture", StyleSet = "suggestedmini",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "3B3B3B", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key="suggstedmini", Value="artsCulture", MatchType="equals"},
            }},
            new StyleEntry() { IsGameElement = true, MatchOrder = 7, Name ="serverGenerated", StyleSet = "suggestedmini",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "CC76E3E1", FillOrStroke = "fill", LineWidthDegrees=0.0000625F, LinePattern= "solid", LayerId = 100 },
                    new StylePaint() { HtmlColorCode = "CC5CB5B4", FillOrStroke = "stroke", LineWidthDegrees=0.0001875F, LinePattern= "solid", LayerId = 99 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key="suggstedmini", Value="generated", MatchType="equals"},
            }},
            //background is a mandatory style entry name, but its transparent here..
            new StyleEntry() { MatchOrder = 10000, Name ="background",  StyleSet = "suggestedmini",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "00000000", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 101 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "bg", Value = "bg", MatchType = "equals"}, //this one only gets called by name anyways.
            }},
            //this name needs to exist because of the default style using this name.
            new StyleEntry() { MatchOrder = 10001, Name ="unmatched",  StyleSet = "suggestedmini",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "00000000", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "a", Value = "s", MatchType = "equals" }}
            },
        };
    }
}
