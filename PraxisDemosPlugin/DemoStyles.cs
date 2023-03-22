using static PraxisCore.DbTables;

namespace PraxisDemosPlugin
{
    public static class DemoStyles
    {
        //Splatter: These styles line up to the index of colors.
        //List of colors is taken from https://lospec.com/palette-list/resurrect-32
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
                    new StylePaint() { HtmlColorCode = "88fb6b1d", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 99 },
                    new StylePaint() { HtmlColorCode = "fb6b1d", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "color", Value = "1", MatchType = "equals"},
            }},
            new StyleEntry() { MatchOrder = 3, Name ="2",  StyleSet = "splatter",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "88e83b3b", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 99 },
                    new StylePaint() { HtmlColorCode = "e83b3b", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "color", Value = "2", MatchType = "equals"},
            }},
            new StyleEntry() { MatchOrder = 4, Name ="3",  StyleSet = "splatter",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "88831c5d", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 99 },
                    new StylePaint() { HtmlColorCode = "831c5d", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "color", Value = "3", MatchType = "equals"},
            }},
            new StyleEntry() { MatchOrder = 5, Name ="4",  StyleSet = "splatter",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "888ff8e2", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 99 },
                    new StylePaint() { HtmlColorCode = "8ff8e2", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "color", Value = "4", MatchType = "equals"},
            }},
            new StyleEntry() { MatchOrder = 6, Name ="5",  StyleSet = "splatter",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "88c32454", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 99 },
                    new StylePaint() { HtmlColorCode = "c32454", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "color", Value = "5", MatchType = "equals"},
            }},
            new StyleEntry() { MatchOrder = 7, Name ="6",  StyleSet = "splatter",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "88f04f78", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 99 },
                    new StylePaint() { HtmlColorCode = "f04f78", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "color", Value = "6", MatchType = "equals"},
            }},
            new StyleEntry() { MatchOrder = 8, Name ="7",  StyleSet = "splatter",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "88f68181", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 99 },
                    new StylePaint() { HtmlColorCode = "f68181", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "color", Value = "7", MatchType = "equals"},
            }},
            new StyleEntry() { MatchOrder = 9, Name ="8",  StyleSet = "splatter",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "88fca790", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 99 },
                    new StylePaint() { HtmlColorCode = "fca790", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "color", Value = "8", MatchType = "equals"},
            }},
            new StyleEntry() { MatchOrder = 10, Name ="9",  StyleSet = "splatter",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "88e3c896", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 99 },
                    new StylePaint() { HtmlColorCode = "e3c896", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "color", Value = "9", MatchType = "equals"},
            }},
            new StyleEntry() { MatchOrder = 11, Name ="10",  StyleSet = "splatter",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "88ab947a", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 99 },
                    new StylePaint() { HtmlColorCode = "ab947a", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "color", Value = "10", MatchType = "equals"},
            }},
            new StyleEntry() { MatchOrder = 12, Name ="11",  StyleSet = "splatter",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "88966c6c", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 99 },
                    new StylePaint() { HtmlColorCode = "966c6c", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "color", Value = "11", MatchType = "equals"},
            }},
            new StyleEntry() { MatchOrder = 13, Name ="12",  StyleSet = "splatter",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "88625565", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 99 },
                    new StylePaint() { HtmlColorCode = "625565", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "color", Value = "12", MatchType = "equals"},
            }},
            new StyleEntry() { MatchOrder = 14, Name ="13",  StyleSet = "splatter",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "883e3546", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 99 },
                    new StylePaint() { HtmlColorCode = "3e3546", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "color", Value = "13", MatchType = "equals"},
            }},
            new StyleEntry() { MatchOrder = 15, Name ="14",  StyleSet = "splatter",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "880b5e65", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 99 },
                    new StylePaint() { HtmlColorCode = "0b5e65", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "color", Value = "14", MatchType = "equals"},
            }},
            new StyleEntry() { MatchOrder = 16, Name ="15",  StyleSet = "splatter",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "880b8a8f", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 99 },
                    new StylePaint() { HtmlColorCode = "0b8a8f", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "color", Value = "15", MatchType = "equals"},
            }},
            new StyleEntry() { MatchOrder = 17, Name ="16",  StyleSet = "splatter",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "881ebc73", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 99 },
                    new StylePaint() { HtmlColorCode = "1ebc73", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "color", Value = "16", MatchType = "equals"},
            }},
            new StyleEntry() { MatchOrder = 18, Name ="17",  StyleSet = "splatter",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "8891db69", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 99 },
                    new StylePaint() { HtmlColorCode = "91db69", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "color", Value = "17", MatchType = "equals"},
            }},
            new StyleEntry() { MatchOrder = 19, Name ="18",  StyleSet = "splatter",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "88fbff86", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 99 },
                    new StylePaint() { HtmlColorCode = "fbff86", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "color", Value = "18", MatchType = "equals"},
            }},
            new StyleEntry() { MatchOrder = 20, Name ="19",  StyleSet = "splatter",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "88fbb954", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 99 },
                    new StylePaint() { HtmlColorCode = "fbb954", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "color", Value = "19", MatchType = "equals"},
            }},
            new StyleEntry() { MatchOrder = 21, Name ="20",  StyleSet = "splatter",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "88cd683d", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 99 },
                    new StylePaint() { HtmlColorCode = "cd683d", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "color", Value = "20", MatchType = "equals"},
            }},
            new StyleEntry() { MatchOrder = 22, Name ="21",  StyleSet = "splatter",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "889e4539", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 99 },
                    new StylePaint() { HtmlColorCode = "9e4539", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "color", Value = "21", MatchType = "equals"},
            }},
            new StyleEntry() { MatchOrder = 23, Name ="22",  StyleSet = "splatter",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "887a3045", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 99 },
                    new StylePaint() { HtmlColorCode = "7a3045", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "color", Value = "22", MatchType = "equals"},
            }},
            new StyleEntry() { MatchOrder = 24, Name ="23",  StyleSet = "splatter",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "886b3e75", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 99 },
                    new StylePaint() { HtmlColorCode = "6b3e75", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "color", Value = "23", MatchType = "equals"},
            }},
            new StyleEntry() { MatchOrder = 25, Name ="24",  StyleSet = "splatter",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "88905ea9", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 99 },
                    new StylePaint() { HtmlColorCode = "905ea9", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "color", Value = "24", MatchType = "equals"},
            }},
            new StyleEntry() { MatchOrder = 26, Name ="25",  StyleSet = "splatter",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "88a884f3", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 99 },
                    new StylePaint() { HtmlColorCode = "a884f3", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "color", Value = "25", MatchType = "equals"},
            }},
            new StyleEntry() { MatchOrder = 27, Name ="26",  StyleSet = "splatter",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "88eaaded", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 99 },
                    new StylePaint() { HtmlColorCode = "eaaded", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "color", Value = "26", MatchType = "equals"},
            }},
            new StyleEntry() { MatchOrder = 28, Name ="27",  StyleSet = "splatter",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "888fd3ff", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 99 },
                    new StylePaint() { HtmlColorCode = "8fd3ff", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "color", Value = "27", MatchType = "equals"},
            }},
            new StyleEntry() { MatchOrder = 29, Name ="28",  StyleSet = "splatter",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "8830e1b9", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 99 },
                    new StylePaint() { HtmlColorCode = "30e1b9", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "color", Value = "28", MatchType = "equals"},
            }},
            new StyleEntry() { MatchOrder = 30, Name ="29",  StyleSet = "splatter",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "884d9be6", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 99 },
                    new StylePaint() { HtmlColorCode = "4d9be6", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "color", Value = "29", MatchType = "equals"},
            }},
            new StyleEntry() { MatchOrder = 31, Name ="30",  StyleSet = "splatter",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "884d65b4", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 99 },
                    new StylePaint() { HtmlColorCode = "4d65b4", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "color", Value = "30", MatchType = "equals"},
            }},
            new StyleEntry() { MatchOrder = 32, Name ="31",  StyleSet = "splatter",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "88484a77", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 99 },
                    new StylePaint() { HtmlColorCode = "484a77", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
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
