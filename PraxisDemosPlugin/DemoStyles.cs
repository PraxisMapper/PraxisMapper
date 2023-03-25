using static PraxisCore.DbTables;

namespace PraxisDemosPlugin
{
    public static class DemoStyles
    {
        //Splatter: These styles line up to the index of colors.
        //List of colors is taken from https://lospec.com/palette-list/resurrect-32 and had saturation boosted 40% to make up for transparency.
        public static List<StyleEntry> splatterStyle = new List<StyleEntry>()
        {
            new StyleEntry() { MatchOrder = 1, Name ="0",  StyleSet = "splatter",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "88ffffff", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 99 },
                    new StylePaint() { HtmlColorCode = "ffffff", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "color", Value = "0", MatchType = "equals"},
            }},
            new StyleEntry() { MatchOrder = 2, Name ="1",   StyleSet = "splatter",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "88ff4f00", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 99 },
                    new StylePaint() { HtmlColorCode = "ff4f00", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "color", Value = "1", MatchType = "equals"},
            }},
            new StyleEntry() { MatchOrder = 3, Name ="2",  StyleSet = "splatter",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "88ff0000", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 99 },
                    new StylePaint() { HtmlColorCode = "ff0000", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "color", Value = "2", MatchType = "equals"},
            }},
            new StyleEntry() { MatchOrder = 4, Name ="3",  StyleSet = "splatter",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "88930065", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 99 },
                    new StylePaint() { HtmlColorCode = "930065", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "color", Value = "3", MatchType = "equals"},
            }},
            new StyleEntry() { MatchOrder = 5, Name ="4",  StyleSet = "splatter",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "8853fee3", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 99 },
                    new StylePaint() { HtmlColorCode = "53fee3", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "color", Value = "4", MatchType = "equals"},
            }},
            new StyleEntry() { MatchOrder = 6, Name ="5",  StyleSet = "splatter",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "88dd0053", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 99 },
                    new StylePaint() { HtmlColorCode = "dd0053", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "color", Value = "5", MatchType = "equals"},
            }},
            new StyleEntry() { MatchOrder = 7, Name ="6",  StyleSet = "splatter",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "88ff0f6f", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 99 },
                    new StylePaint() { HtmlColorCode = "ff0f6f", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "color", Value = "6", MatchType = "equals"},
            }},
            new StyleEntry() { MatchOrder = 8, Name ="7",  StyleSet = "splatter",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "88ff6e6e", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 99 },
                    new StylePaint() { HtmlColorCode = "ff6e6e", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "color", Value = "7", MatchType = "equals"},
            }},
            new StyleEntry() { MatchOrder = 9, Name ="8",  StyleSet = "splatter",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "88ff9d78", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 99 },
                    new StylePaint() { HtmlColorCode = "ff9d78", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "color", Value = "8", MatchType = "equals"},
            }},
            new StyleEntry() { MatchOrder = 10, Name ="9",  StyleSet = "splatter",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "88ebc677", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 99 },
                    new StylePaint() { HtmlColorCode = "ebc677", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "color", Value = "9", MatchType = "equals"},
            }},
            new StyleEntry() { MatchOrder = 11, Name ="10",  StyleSet = "splatter",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "88b3946d", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 99 },
                    new StylePaint() { HtmlColorCode = "b3946d", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "color", Value = "10", MatchType = "equals"},
            }},
            new StyleEntry() { MatchOrder = 12, Name ="11",  StyleSet = "splatter",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "88a06868", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 99 },
                    new StylePaint() { HtmlColorCode = "a06868", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "color", Value = "11", MatchType = "equals"},
            }},
            new StyleEntry() { MatchOrder = 13, Name ="12",  StyleSet = "splatter",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "88655369", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 99 },
                    new StylePaint() { HtmlColorCode = "655369", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "color", Value = "12", MatchType = "equals"},
            }},
            new StyleEntry() { MatchOrder = 14, Name ="13",  StyleSet = "splatter",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "8840344b", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 99 },
                    new StylePaint() { HtmlColorCode = "40344b", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "color", Value = "13", MatchType = "equals"},
            }},
            new StyleEntry() { MatchOrder = 15, Name ="14",  StyleSet = "splatter",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "8800656e", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 99 },
                    new StylePaint() { HtmlColorCode = "00656e", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "color", Value = "14", MatchType = "equals"},
            }},
            new StyleEntry() { MatchOrder = 16, Name ="15",  StyleSet = "splatter",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "88009296", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 99 },
                    new StylePaint() { HtmlColorCode = "009296", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "color", Value = "15", MatchType = "equals"},
            }},
            new StyleEntry() { MatchOrder = 17, Name ="16",  StyleSet = "splatter",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "8800c455", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 99 },
                    new StylePaint() { HtmlColorCode = "00c455", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "color", Value = "16", MatchType = "equals"},
            }},
            new StyleEntry() { MatchOrder = 18, Name ="17",  StyleSet = "splatter",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "8872e300", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 99 },
                    new StylePaint() { HtmlColorCode = "72e300", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "color", Value = "17", MatchType = "equals"},
            }},
            new StyleEntry() { MatchOrder = 19, Name ="18",  StyleSet = "splatter",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "88fbff00", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 99 },
                    new StylePaint() { HtmlColorCode = "fbff00", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "color", Value = "18", MatchType = "equals"},
            }},
            new StyleEntry() { MatchOrder = 20, Name ="19",  StyleSet = "splatter",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "88ffb400", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 99 },
                    new StylePaint() { HtmlColorCode = "ffb400", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "color", Value = "19", MatchType = "equals"},
            }},
            new StyleEntry() { MatchOrder = 21, Name ="20",  StyleSet = "splatter",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "88e25800", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 99 },
                    new StylePaint() { HtmlColorCode = "e25800", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "color", Value = "20", MatchType = "equals"},
            }},
            new StyleEntry() { MatchOrder = 22, Name ="21",  StyleSet = "splatter",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "88af3617", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 99 },
                    new StylePaint() { HtmlColorCode = "af3617", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "color", Value = "21", MatchType = "equals"},
            }},
            new StyleEntry() { MatchOrder = 23, Name ="22",  StyleSet = "splatter",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "88882144", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 99 },
                    new StylePaint() { HtmlColorCode = "882144", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "color", Value = "22", MatchType = "equals"},
            }},
            new StyleEntry() { MatchOrder = 24, Name ="23",  StyleSet = "splatter",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "88743581", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 99 },
                    new StylePaint() { HtmlColorCode = "743581", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "color", Value = "23", MatchType = "equals"},
            }},
            new StyleEntry() { MatchOrder = 25, Name ="24",  StyleSet = "splatter",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "889c59ba", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 99 },
                    new StylePaint() { HtmlColorCode = "9c59ba", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "color", Value = "24", MatchType = "equals"},
            }},
            new StyleEntry() { MatchOrder = 26, Name ="25",  StyleSet = "splatter",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "88b07cff", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 99 },
                    new StylePaint() { HtmlColorCode = "b07cff", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "color", Value = "25", MatchType = "equals"},
            }},
            new StyleEntry() { MatchOrder = 27, Name ="26",  StyleSet = "splatter",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "88f8a4fc", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 99 },
                    new StylePaint() { HtmlColorCode = "f8a4fc", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "color", Value = "26", MatchType = "equals"},
            }},
            new StyleEntry() { MatchOrder = 28, Name ="27",  StyleSet = "splatter",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "886bd8ff", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 99 },
                    new StylePaint() { HtmlColorCode = "6bd8ff", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "color", Value = "27", MatchType = "equals"},
            }},
            new StyleEntry() { MatchOrder = 29, Name ="28",  StyleSet = "splatter",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "8800eab3", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 99 },
                    new StylePaint() { HtmlColorCode = "00eab3", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "color", Value = "28", MatchType = "equals"},
            }},
            new StyleEntry() { MatchOrder = 30, Name ="29",  StyleSet = "splatter",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "88009ffe", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 99 },
                    new StylePaint() { HtmlColorCode = "009ffe", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "color", Value = "29", MatchType = "equals"},
            }},
            new StyleEntry() { MatchOrder = 31, Name ="30",  StyleSet = "splatter",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "884669cb", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 99 },
                    new StylePaint() { HtmlColorCode = "4669cb", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "color", Value = "30", MatchType = "equals"},
            }},
            new StyleEntry() { MatchOrder = 32, Name ="31",  StyleSet = "splatter",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "88464983", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 99 },
                    new StylePaint() { HtmlColorCode = "464983", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "color", Value = "31", MatchType = "equals"},
            }},

            //background is a mandatory style entry name, but its transparent here..
            new StyleEntry() { MatchOrder = 10000, Name ="background",  StyleSet = "splatter",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "00000000", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 101 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "*", Value = "*", MatchType = "default"},
            }},
        };
    }
}
