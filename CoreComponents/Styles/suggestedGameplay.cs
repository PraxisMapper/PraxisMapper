﻿using System.Collections.Generic;
using static PraxisCore.DbTables;

namespace PraxisCore.Styles
{
    /// <summary>
    /// An abbreviated, bolder-colored version of the default mapTiles style, that only draws elements likely to be good places for gameplay.
    /// </summary>
    public static class suggestedGameplay
    {
        public static List<StyleEntry> style = new List<StyleEntry>()
        {
            new StyleEntry() { IsGameElement = true,  MatchOrder = 1, Name ="water", StyleSet = "suggestedGameplay",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "CC0062FF", FillOrStroke = "fill", LineWidthDegrees=0.0000625F, LinePattern= "solid", LayerId = 100 },
                    new StylePaint() { HtmlColorCode = "CC0000FF", FillOrStroke = "stroke", LineWidthDegrees=0.0001875F, LinePattern= "solid", LayerId = 99 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "natural", Value = "water|strait|bay", MatchType = "or"},
                    new StyleMatchRule() {Key = "waterway", Value ="*", MatchType="or" },
                    new StyleMatchRule() {Key = "landuse", Value ="basin", MatchType="or" },
                    new StyleMatchRule() {Key = "leisure", Value ="swimming_pool", MatchType="or" },
                    new StyleMatchRule() {Key = "place", Value ="sea", MatchType="or" }, //stupid Labrador sea value.
                }},
            new StyleEntry() { IsGameElement = true, MatchOrder = 2, Name ="wetland", StyleSet = "suggestedGameplay",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "CC609124", FillOrStroke = "fill", LineWidthDegrees=0.0000625F, LinePattern= "solid", LayerId = 100 },
                    new StylePaint() { HtmlColorCode = "CC289124", FillOrStroke = "stroke", LineWidthDegrees=0.0001875F, LinePattern= "solid", LayerId = 99 }
                },
                 StyleMatchRules = new List<StyleMatchRule>() {
                     new StyleMatchRule() { Key = "natural", Value = "wetland", MatchType = "equals" }
                 }},
            new StyleEntry() { IsGameElement = true, MatchOrder = 3, Name ="park", StyleSet = "suggestedGameplay",
                PaintOperations = new List<StylePaint>() {

                    new StylePaint() { HtmlColorCode = "CC93FF61", FillOrStroke = "fill", LineWidthDegrees=0.0000625F, LinePattern= "solid", LayerId = 100 },
                    new StylePaint() { HtmlColorCode = "CCB2FF8F", FillOrStroke = "stroke", LineWidthDegrees=0.0001875F, LinePattern= "solid", LayerId = 99 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "leisure", Value = "park", MatchType = "or" },
                    // new StyleMatchRule() { Key = "leisure", Value = "playground", MatchType = "or" } //picks up school playgrounds, would prefer to discourage that.
            }},
            new StyleEntry() { IsGameElement = true, MatchOrder = 4, Name ="beach", StyleSet = "suggestedGameplay",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "CCF9FF8F", FillOrStroke = "fill", LineWidthDegrees=0.0000625F, LinePattern= "solid", LayerId = 100 },
                    new StylePaint() { HtmlColorCode = "CCFFEA8F", FillOrStroke = "stroke", LineWidthDegrees=0.0001875F, LinePattern= "solid", LayerId = 99 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "natural", Value = "beach|shoal", MatchType = "or" },
                    new StyleMatchRule() {Key = "leisure", Value="beach_resort", MatchType="or"}
            } },
            new StyleEntry() { IsGameElement = true, MatchOrder = 5, Name ="university", StyleSet = "suggestedGameplay",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "CCFFFFE5", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 },
                    new StylePaint() { HtmlColorCode = "CCF5EED3", FillOrStroke = "stroke", LineWidthDegrees=0.0001875F, LinePattern= "solid", LayerId = 99 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "amenity", Value = "university|college", MatchType = "any" }}
            },
            new StyleEntry() { IsGameElement = true, MatchOrder = 6, Name ="natureReserve", StyleSet = "suggestedGameplay",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "CC124504", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 },
                    new StylePaint() { HtmlColorCode = "CC027021", FillOrStroke = "stroke", LineWidthDegrees=0.0001875F, LinePattern= "solid", LayerId = 99 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "leisure", Value = "nature_reserve", MatchType = "equals" }}
            },
            new StyleEntry() {IsGameElement = true, MatchOrder = 7, Name ="cemetery", StyleSet = "suggestedGameplay",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "CCAACBAF", FillOrStroke = "fill", FileName="Landuse-cemetery.png", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 },
                    new StylePaint() { HtmlColorCode = "CC404040", FillOrStroke = "stroke", LineWidthDegrees=0.0001875F, LinePattern= "solid", LayerId = 99 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "landuse", Value = "cemetery", MatchType = "or" },
                    new StyleMatchRule() {Key="amenity", Value="grave_yard", MatchType="or" } }
            },
            new StyleEntry() { IsGameElement = true, MatchOrder = 8, Name ="retail", StyleSet = "suggestedGameplay",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "CCF2BBE9", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 },
                    new StylePaint() { HtmlColorCode = "CCF595E5", FillOrStroke = "stroke", LineWidthDegrees=0.0001875F, LinePattern= "solid", LayerId = 99 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "landuse", Value = "retail|commercial", MatchType = "or"},
                    new StyleMatchRule() {Key="building", Value="retail|commercial", MatchType="or" },
                    new StyleMatchRule() {Key="shop", Value="*", MatchType="or" }
            }},
            new StyleEntry() { IsGameElement = true, MatchOrder = 11, Name ="historical", StyleSet = "suggestedGameplay",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "CCB3B3B3", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 },
                    new StylePaint() { HtmlColorCode = "CC9D9D9D", FillOrStroke = "stroke", LineWidthDegrees=0.0001875F, LinePattern= "solid", LayerId = 99 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "historic", Value = "*", MatchType = "equals" }}
            },
            new StyleEntry() { IsGameElement = true, MatchOrder = 12, Name ="trail", StyleSet = "suggestedGameplay",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "CC7A5206", FillOrStroke = "stroke", LineWidthDegrees=0.0001875F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key="highway", Value="path|bridleway|cycleway|footway|living_street", MatchType="any"},
                    new StyleMatchRule() { Key="footway", Value="sidewalk|crossing", MatchType="not"}
            }},
            new StyleEntry() { IsGameElement = true, MatchOrder = 13, Name ="concert hall", StyleSet = "suggestedGameplay",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "3B3B3B", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "amenity", Value = "concert hall", MatchType = "or" },
                    new StyleMatchRule() { Key = "theatre:type", Value = "concert_hall", MatchType = "or" },
            }},
            new StyleEntry() { IsGameElement = true, MatchOrder = 14, Name ="theatre", StyleSet = "suggestedGameplay",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "3B3B3B", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "amenity", Value = "theatre", MatchType = "equals" },
            }},
            new StyleEntry() { IsGameElement = true, MatchOrder = 15, Name ="arts centre", StyleSet = "suggestedGameplay",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "3B3B3B", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "amenity", Value = "arts_centre", MatchType = "equals" },
            }},
            new StyleEntry() { IsGameElement = true, MatchOrder = 16, Name ="planetarium", StyleSet = "suggestedGameplay",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "3B3B3B", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "amenity", Value = "planetarium", MatchType = "equals" },
            }},
            new StyleEntry() { IsGameElement = true, MatchOrder = 17, Name ="artsCulture", StyleSet = "suggestedGameplay",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "3B3B3B", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "amenity", Value = "planetarium", MatchType = "equals" },
            }},
            new StyleEntry() { IsGameElement = true, MatchOrder = 18, Name ="library", StyleSet = "suggestedGameplay",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "3B3B3B", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "amenity", Value = "library", MatchType = "equals" },
            }},
            new StyleEntry() { IsGameElement = true, MatchOrder = 19, Name ="public bookcase", StyleSet = "suggestedGameplay",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "3B3B3B", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "amenity", Value = "public_bookcase", MatchType = "equals" },
            }},
            new StyleEntry() { IsGameElement = true, MatchOrder = 20, Name ="community centre", StyleSet = "suggestedGameplay",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "3B3B3B", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "amenity", Value = "community_centre", MatchType = "equals" },
            }},
            new StyleEntry() { IsGameElement = true, MatchOrder = 21, Name ="conference centre", StyleSet = "suggestedGameplay",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "3B3B3B", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "amenity", Value = "conference_centre", MatchType = "equals" },
            }},
            new StyleEntry() { IsGameElement = true, MatchOrder = 22, Name ="exhibition centre", StyleSet = "suggestedGameplay",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "3B3B3B", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "amenity", Value = "exhibition_centre", MatchType = "equals" },
            }},
            new StyleEntry() { IsGameElement = true, MatchOrder = 23, Name ="events_venue", StyleSet = "suggestedGameplay",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "3B3B3B", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "amenity", Value = "events_venue", MatchType = "equals" },
            }},
            new StyleEntry() { IsGameElement = true, MatchOrder = 24, Name ="aquarium", StyleSet = "suggestedGameplay",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "3B3B3B", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "tourism", Value = "aquarium", MatchType = "equals" },
            }},
            new StyleEntry() { IsGameElement = true, MatchOrder = 25, Name ="artwork", StyleSet = "suggestedGameplay",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "3B3B3B", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "tourism", Value = "artwork", MatchType = "equals" },
            }},
            new StyleEntry() { IsGameElement = true, MatchOrder = 26, Name ="attraction", StyleSet = "suggestedGameplay",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "3B3B3B", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "tourism", Value = "attraction", MatchType = "equals" },
            }},
            new StyleEntry() { IsGameElement = true, MatchOrder = 27, Name ="gallery", StyleSet = "suggestedGameplay",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "3B3B3B", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "tourism", Value = "gallery", MatchType = "equals" },
            }},
            new StyleEntry() { IsGameElement = true, MatchOrder = 28, Name ="museum", StyleSet = "suggestedGameplay",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "3B3B3B", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "tourism", Value = "museum", MatchType = "equals" },
            }},
            new StyleEntry() { IsGameElement = true, MatchOrder = 29, Name ="theme_park", StyleSet = "suggestedGameplay",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "3B3B3B", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "tourism", Value = "theme_park", MatchType = "equals" },
            }},
            new StyleEntry() { IsGameElement = true, MatchOrder = 30, Name ="viewpoint", StyleSet = "suggestedGameplay",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "3B3B3B", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "tourism", Value = "viewpoint", MatchType = "equals" },
            }},
            new StyleEntry() { IsGameElement = true, MatchOrder = 31, Name ="zoo", StyleSet = "suggestedGameplay",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "3B3B3B", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "tourism", Value = "zoo", MatchType = "equals" },
            }},
            new StyleEntry() { IsGameElement = true, MatchOrder = 9980, Name ="serverGenerated", StyleSet = "suggestedGameplay",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "CC76E3E1", FillOrStroke = "fill", LineWidthDegrees=0.0000625F, LinePattern= "solid", LayerId = 100 },
                    new StylePaint() { HtmlColorCode = "CC5CB5B4", FillOrStroke = "stroke", LineWidthDegrees=0.0001875F, LinePattern= "solid", LayerId = 99 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key="generated", Value="praxisMapper", MatchType="equals"},
            }},
            //background is a mandatory style entry name, but its transparent here..
            new StyleEntry() { MatchOrder = 10000, Name ="background",  StyleSet = "suggestedGameplay",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "FFFFFF", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 101 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "bg", Value = "bg", MatchType = "equals"}, //this one only gets called by name anyways.
            }},
            //this name needs to exist because of the default style using this name.
            new StyleEntry() { MatchOrder = 10001, Name ="unmatched",  StyleSet = "suggestedGameplay",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "00000000", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "a", Value = "s", MatchType = "equals" }}
            },
        };
    }
}
