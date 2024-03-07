using System.Collections.Generic;
using static PraxisCore.DbTables;

namespace PraxisCore.Styles
{
    /// <summary>
    /// The baseline map tile style, based on OSMCarto
    /// </summary>
    public static class mapTiles
    {
        public static List<StyleEntry> style = new List<StyleEntry>() { 
            //NOTE:
            //Drawing order rules: bigger numbers are drawn first == smaller numbers get drawn over bigger numbers for LayerId. Smaller areas will be drawn after (on top of) larger areas in the same layer.
            //1001: background (always present, added into the draw list by processing.)
            //1000: unmatched (transparent)
            //101: water polygons processed from shapefiles (should always be drawn under other elements)
            //100: MOST area elements.
            //99: tertiary top-layer, area element outlines (when an area has an outline)
            //98: tertiary bottom-layer
            //97: secondary top, tree top
            //96: secondary bottom, tree bottom
            //95: primary top
            //94: primary bottom
            //93: motorway top
            //92: motorway bottom
            //60-70: admin boundaries, if they weren't transparent.


            //NOTE: a rough analysis suggests that about 1/3 of entries are 'tertiary' roads and another third are 'building'
            //BUT buildings can often be marked with another interesting property, like retail, tourism, or historical, and I'd prefer those match first.
            //So I will re-order this to handle tertiary first, then the gameElements that might be buildings, then plain buildings, and the remaining elements after that.
            //This should mean that 60%+ of elements match in 6 checks or less. 
            //MapTiles: Roads of varying sizes and colors to match OSM colors
            new StyleEntry() { MatchOrder = 10, Name ="tertiary", StyleSet = "mapTiles", //This is MatchOrder 10 because its one of the most common entries, is the correct answer 30% of the time.
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "ffffff", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 98, MaxDrawRes = ConstantValues.zoom10DegPerPixelX / 2},
                    new StylePaint() { HtmlColorCode = "8f8f8f", FillOrStroke = "stroke", LineWidthDegrees=0.0000375F, LinePattern= "solid", LayerId = 99, MaxDrawRes = ConstantValues.zoom10DegPerPixelX / 2}
                },
                StyleMatchRules = new List<StyleMatchRule>()
            {
                new StyleMatchRule() { Key = "highway", Value = "tertiary|unclassified|residential|tertiary_link|service|road", MatchType = "any" },
                new StyleMatchRule() { Key="footway", Value="sidewalk|crossing", MatchType="not"},
            }},
            new StyleEntry() { IsGameElement = true, MatchOrder = 20, Name ="university", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "FFFFE5", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "amenity", Value = "university|college", MatchType = "any" }}
            },
            new StyleEntry() { MatchOrder = 30, Name ="retail", StyleSet = "mapTiles", IsGameElement = true,
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "FFD4CE", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "landuse", Value = "retail|commercial", MatchType = "or"},
                    new StyleMatchRule() {Key="building", Value="retail|commercial", MatchType="or" },
                    new StyleMatchRule() {Key="shop", Value="*", MatchType="or" }
            }},
            new StyleEntry() { IsGameElement = true, MatchOrder = 40, Name ="theatre", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "3B3B3B", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "amenity", Value = "theatre", MatchType = "equals" },
            }},
            new StyleEntry() { IsGameElement = true, MatchOrder = 50, Name ="concert hall", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "3B3B3B", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "amenity", Value = "concert hall", MatchType = "equals" },
            }},
            new StyleEntry() { IsGameElement = true, MatchOrder = 60, Name ="arts centre", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "3B3B3B", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "amenity", Value = "arts_centre", MatchType = "equals" },
            }},
            new StyleEntry() { IsGameElement = true, MatchOrder = 70, Name ="planetarium", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "3B3B3B", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "amenity", Value = "planetarium", MatchType = "equals" },
            }},
            new StyleEntry() { IsGameElement = true, MatchOrder = 80, Name ="artsCulture", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "3B3B3B", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "amenity", Value = "planetarium", MatchType = "equals" },
            }},
            new StyleEntry() { IsGameElement = true, MatchOrder = 90, Name ="library", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "3B3B3B", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "amenity", Value = "library", MatchType = "equals" },
            }},
            new StyleEntry() { IsGameElement = true, MatchOrder = 100, Name ="public bookcase", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "3B3B3B", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "amenity", Value = "public_bookcase", MatchType = "equals" },
            }},
            new StyleEntry() { IsGameElement = true, MatchOrder = 110, Name ="community centre", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "3B3B3B", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "amenity", Value = "community_centre", MatchType = "equals" },
            }},
            new StyleEntry() { IsGameElement = true, MatchOrder = 120, Name ="conference centre", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "3B3B3B", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "amenity", Value = "conference_centre", MatchType = "equals" },
            }},
            new StyleEntry() { IsGameElement = true, MatchOrder = 130, Name ="exhibition centre", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "3B3B3B", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "amenity", Value = "exhibition_centre", MatchType = "equals" },
            }},
            new StyleEntry() { IsGameElement = true, MatchOrder = 140, Name ="events_venue", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "3B3B3B", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "amenity", Value = "events_venue", MatchType = "equals" },
            }},
            new StyleEntry() { IsGameElement = true, MatchOrder = 150, Name ="aquarium", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "3B3B3B", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "tourism", Value = "aquarium", MatchType = "equals" },
            }},
            new StyleEntry() { IsGameElement = true, MatchOrder = 160, Name ="artwork", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "3B3B3B", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "tourism", Value = "artwork", MatchType = "equals" },
            }},
            new StyleEntry() { IsGameElement = true, MatchOrder = 170, Name ="attraction", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "3B3B3B", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "tourism", Value = "attraction", MatchType = "equals" },
            }},
            new StyleEntry() { IsGameElement = true, MatchOrder = 180, Name ="gallery", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "3B3B3B", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "tourism", Value = "gallery", MatchType = "equals" },
            }},
            new StyleEntry() { IsGameElement = true, MatchOrder = 190, Name ="museum", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "3B3B3B", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "tourism", Value = "museum", MatchType = "equals" },
            }},
            new StyleEntry() { IsGameElement = true, MatchOrder = 200, Name ="theme_park", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "3B3B3B", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "tourism", Value = "theme_park", MatchType = "equals" },
            }},
            new StyleEntry() { IsGameElement = true, MatchOrder = 210, Name ="viewpoint", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "3B3B3B", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "tourism", Value = "viewpoint", MatchType = "equals" },
            }},
            new StyleEntry() { IsGameElement = true, MatchOrder = 220, Name ="zoo", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "3B3B3B", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "tourism", Value = "zoo", MatchType = "equals" },
            }},
            new StyleEntry() { MatchOrder = 390, Name ="camping", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "def6c0", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100, MaxDrawRes = ConstantValues.zoom10DegPerPixelX / 2},
                },
                StyleMatchRules = new List<StyleMatchRule>()
            {
                new StyleMatchRule() { Key="tourism", Value="camp_site|caravan_site", MatchType="any"},
            }},
            new StyleEntry() { IsGameElement = false, MatchOrder = 400, Name ="tourism", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "660033", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "tourism", Value = "*", MatchType = "equals" }}
            },
            new StyleEntry() { IsGameElement = true, MatchOrder = 490, Name ="wreck", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "A52A2A", FileName="Wreck.png", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "historic", Value = "wreck", MatchType = "equals" }}
            },
            new StyleEntry() { IsGameElement = true, MatchOrder = 500, Name ="historical", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "B3B3B3", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "historic", Value = "*", MatchType = "equals" }}
                //TODO: add a NOT for boundaries. Ireland is not 'historic' in the sense that this tag intends.
            },
            new StyleEntry() { MatchOrder = 650, Name ="placeofworship", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "d0d0d0", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100, MaxDrawRes = ConstantValues.zoom10DegPerPixelX / 2},
                },
                StyleMatchRules = new List<StyleMatchRule>()
            {
                new StyleMatchRule() { Key="amenity", Value="place_of_worship", MatchType="or"},
                new StyleMatchRule() { Key="landuse", Value="religious", MatchType="or"},
            }},
            new StyleEntry() { MatchOrder = 690, Name ="namedBuilding", StyleSet = "mapTiles", IsGameElement = true,
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "d1b6a1", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 },
                    new StylePaint() { HtmlColorCode = "806b5b", FillOrStroke = "stroke", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 99 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "building", Value = "*", MatchType = "any" },
                    new StyleMatchRule() { Key = "name", Value = "*", MatchType = "any" }
                }
            },
            new StyleEntry() { MatchOrder = 700, Name ="building", StyleSet = "mapTiles", //NOTE: making this matchOrder=20 makes map tiles draw faster, but hides some gameplay-element colors.
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "d9d0c9", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 },
                    new StylePaint() { HtmlColorCode = "B8A89C", FillOrStroke = "stroke", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 99 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "building", Value = "*", MatchType = "equals" }} },
            new StyleEntry() { MatchOrder = 790, Name ="bgwater", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "aad3df", FillOrStroke = "fill", LineWidthDegrees=0.0000625F, LinePattern= "solid", LayerId = 101 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "bgwater", Value = "praxismapper", MatchType = "equals"}, //ensures that this specific element was processed by PM for this purpose.
                }},
            new StyleEntry() { MatchOrder = 800, Name ="water", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "aad3df", FillOrStroke = "fill", LineWidthDegrees=0.0000625F, LinePattern= "solid", LayerId = 100 } 
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "natural", Value = "water|strait|bay", MatchType = "or"}, 
                    new StyleMatchRule() {Key = "waterway", Value ="*", MatchType="or" },
                    new StyleMatchRule() {Key = "landuse", Value ="basin", MatchType="or" },
                    new StyleMatchRule() {Key = "leisure", Value ="swimming_pool", MatchType="or" }, 
                    new StyleMatchRule() {Key = "place", Value ="sea", MatchType="or" }, //stupid Labrador sea value.
                }},
            new StyleEntry() {IsGameElement = true,  MatchOrder = 900, Name ="wetland", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "0C4026", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                 StyleMatchRules = new List<StyleMatchRule>() {
                     new StyleMatchRule() { Key = "natural", Value = "wetland", MatchType = "equals" }
                 }},
            new StyleEntry() { IsGameElement = true, MatchOrder = 1000, Name ="park", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "C8FACC", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "leisure", Value = "park", MatchType = "or" },
                    new StyleMatchRule() { Key = "leisure", Value = "playground", MatchType = "or" },
            }},
            new StyleEntry() { IsGameElement = true, MatchOrder = 1100, Name ="beach", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "fff1ba", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "natural", Value = "beach|shoal", MatchType = "or" },
                    new StyleMatchRule() {Key = "leisure", Value="beach_resort", MatchType="or"}
            } },

            new StyleEntry() { IsGameElement = true, MatchOrder = 1200, Name ="natureReserve", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "124504", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "leisure", Value = "nature_reserve", MatchType = "equals" }}
            },
            new StyleEntry() {IsGameElement = true, MatchOrder = 1300, Name ="cemetery", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "AACBAF", FillOrStroke = "fill", FileName="Landuse-cemetery.png", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "landuse", Value = "cemetery", MatchType = "or" },
                    new StyleMatchRule() {Key="amenity", Value="grave_yard", MatchType="or" } }
            },
            new StyleEntry() { MatchOrder = 1400, Name ="trailFilled", StyleSet = "mapTiles", IsGameElement = false, //This exists to make the map look correct, but these are so few removing them as game elements should not impact games.
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "F0E68C", FillOrStroke = "fill", LineWidthDegrees=0.000025F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key="highway", Value="path|bridleway|cycleway|footway|living_street", MatchType="any"},
                    new StyleMatchRule() { Key="footway", Value="sidewalk|crossing", MatchType="not"},
                    new StyleMatchRule() { Key="area", Value="yes", MatchType="equals"}
            }},
            new StyleEntry() { IsGameElement = true, MatchOrder = 1500, Name ="trail", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "F0E68C", FillOrStroke = "stroke", LineWidthDegrees=0.000025F, LinePattern= "solid", LayerId = 100, MaxDrawRes = ConstantValues.zoom14DegPerPixelX }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key="highway", Value="path|bridleway|cycleway|footway|living_street", MatchType="any"},
                    new StyleMatchRule() { Key="footway", Value="sidewalk|crossing", MatchType="not"}
            }},
            //Admin bounds are transparent on mapTiles, because I would prefer to find and skip them on this style. Use the adminBounds style to draw them on their own tile layer.
            //This is removed to make drawing maptiles faster.
            //new StyleEntry() { MatchOrder = 160, Name ="admin", StyleSet = "mapTiles",
            //    PaintOperations = new List<StylePaint>() {
            //        new StylePaint() { HtmlColorCode = "00FF2020", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "10|5", LayerId = 70 }
            //    },
            //    StyleMatchRules = new List<StyleMatchRule>() {
            //        new StyleMatchRule() { Key = "boundary", Value = "administrative", MatchType = "equals" }}
            //},
            new StyleEntry() { MatchOrder = 1700, Name ="parking", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "EEEEEE", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100, MinDrawRes = ConstantValues.zoom12DegPerPixelX}
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "amenity", Value = "parking", MatchType = "equals" }}
            },
            //New generic entries for mapping by color
            new StyleEntry() { MatchOrder = 1800, Name ="grass",  StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "cdebb0", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "landuse", Value = "grass|meadow|recreation_ground|village_green", MatchType = "or" },
                    new StyleMatchRule() { Key = "natural", Value = "heath|grassland", MatchType = "or" },
                    new StyleMatchRule() { Key = "leisure", Value = "garden", MatchType = "or" },
                    new StyleMatchRule() { Key = "golf", Value = "tee|fairway|driving_range", MatchType = "or" },
            }},
            new StyleEntry() { MatchOrder = 1900, Name ="sandColored",  StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "D7B526", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "natural", Value = "shingle|dune|scree", MatchType = "or" },
                    new StyleMatchRule() { Key = "surface", Value = "sand", MatchType = "or" }
            }},
            new StyleEntry() { MatchOrder = 2000, Name ="forest",  StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "ADD19E", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "natural", Value = "wood", MatchType = "or" },
                    new StyleMatchRule() { Key = "landuse", Value = "forest", MatchType = "or" },
            }},
            new StyleEntry() { MatchOrder = 2100, Name ="industrial",  StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "EBDBE8", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "landuse", Value = "industrial", MatchType = "equals" },
            }},
            new StyleEntry() { MatchOrder = 2200, Name ="residential",  StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "009933", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "landuse", Value = "residential", MatchType = "equals" },
            }},
            new StyleEntry() { MatchOrder = 2300, Name ="sidewalk", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "C0C0C0", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "highway", Value = "pedestrian", MatchType = "equals" },
            }},
            //Transparent: we don't usually want to draw census boundaries
            new StyleEntry() { MatchOrder = 2400, Name ="censusbounds",  StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "00000000", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "boundary", Value = "census", MatchType = "equals" },
            }},
            //Transparents: Explicitly things that don't help when drawn in one color.
            new StyleEntry() { MatchOrder = 2500, Name ="donotdraw",  StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "00000000", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "place", Value = "locality|islet", MatchType = "any" },
            }},
            new StyleEntry() { MatchOrder = 2600, Name ="greyFill",  StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "AAAAAA", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "man_made", Value = "breakwater", MatchType = "any" },
            }},

            //Roads of varying sizes and colors to match OSM colors
            new StyleEntry() { MatchOrder = 2700, Name ="motorwayFilled", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "e892a2", FillOrStroke = "fill", LineWidthDegrees=0.000125F, LinePattern= "solid", LayerId = 92},
                    new StylePaint() { HtmlColorCode = "dc2a67", FillOrStroke = "fill", LineWidthDegrees=0.000155F, LinePattern= "solid", LayerId = 93}
                },
                StyleMatchRules = new List<StyleMatchRule>()
            {
                new StyleMatchRule() { Key = "highway", Value = "motorway|trunk|motorway_link|trunk_link", MatchType = "any" },
                new StyleMatchRule() { Key="footway", Value="sidewalk|crossing", MatchType="not"},
                new StyleMatchRule() { Key="area", Value="yes", MatchType="equals"}
            }},
            //Roads of varying sizes and colors to match OSM colors
            new StyleEntry() { MatchOrder = 2800, Name ="motorway", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "e892a2", FillOrStroke = "fill", LineWidthDegrees=0.000125F, LinePattern= "solid", LayerId = 92},
                    new StylePaint() { HtmlColorCode = "dc2a67", FillOrStroke = "fill", LineWidthDegrees=0.000155F, LinePattern= "solid", LayerId = 93}
                },
                StyleMatchRules = new List<StyleMatchRule>()
            {
                new StyleMatchRule() { Key = "highway", Value = "motorway|trunk|motorway_link|trunk_link", MatchType = "any" },
                new StyleMatchRule() { Key="footway", Value="sidewalk|crossing", MatchType="not"},
            }},
            new StyleEntry() { MatchOrder = 2900, Name ="primaryFilled", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "fcd6a4", FillOrStroke = "fill", LineWidthDegrees=0.000025F, LinePattern= "solid", LayerId = 94, },
                    new StylePaint() { HtmlColorCode = "a06b00", FillOrStroke = "fill", LineWidthDegrees=0.00004275F, LinePattern= "solid", LayerId = 95, }
                },
                StyleMatchRules = new List<StyleMatchRule>()
            {
                new StyleMatchRule() { Key = "highway", Value = "primary|primary_link", MatchType = "any" },
                new StyleMatchRule() { Key="footway", Value="sidewalk|crossing", MatchType="not"},
                new StyleMatchRule() { Key="area", Value="yes", MatchType="equals"}
            }},
            new StyleEntry() { MatchOrder = 3000, Name ="primary", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "fcd6a4", FillOrStroke = "stroke", LineWidthDegrees=0.00005F, LinePattern= "solid", LayerId = 94, MaxDrawRes = ConstantValues.zoom6DegPerPixelX /2 },
                    new StylePaint() { HtmlColorCode = "a06b00", FillOrStroke = "stroke", LineWidthDegrees=0.000085F, LinePattern= "solid", LayerId = 95, MaxDrawRes = ConstantValues.zoom6DegPerPixelX /2}
                },
                StyleMatchRules = new List<StyleMatchRule>()
            {
                new StyleMatchRule() { Key = "highway", Value = "primary|primary_link", MatchType = "any" },
                new StyleMatchRule() { Key="footway", Value="sidewalk|crossing", MatchType="not"},
            }},
            new StyleEntry() { MatchOrder = 3100, Name ="secondaryFilled",  StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "f7fabf", FillOrStroke = "fill", LineWidthDegrees=0.0000375F, LinePattern= "solid", LayerId = 96, MaxDrawRes = ConstantValues.zoom8DegPerPixelX,},
                    new StylePaint() { HtmlColorCode = "707d05", FillOrStroke = "fill", LineWidthDegrees=0.0000625F, LinePattern= "solid", LayerId = 97, MaxDrawRes = ConstantValues.zoom8DegPerPixelX,}
                },
                StyleMatchRules = new List<StyleMatchRule>()
            {
                new StyleMatchRule() { Key = "highway", Value = "secondary|secondary_link", MatchType = "any" },
                new StyleMatchRule() { Key="footway", Value="sidewalk|crossing", MatchType="not"},
                new StyleMatchRule() { Key="area", Value="yes", MatchType="equals"}
            }},
            new StyleEntry() { MatchOrder = 3200, Name ="secondary",  StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "f7fabf", FillOrStroke = "stroke", LineWidthDegrees=0.0000375F, LinePattern= "solid", LayerId = 96, MaxDrawRes = ConstantValues.zoom8DegPerPixelX,},
                    new StylePaint() { HtmlColorCode = "707d05", FillOrStroke = "stroke", LineWidthDegrees=0.0000625F, LinePattern= "solid", LayerId = 97, MaxDrawRes = ConstantValues.zoom8DegPerPixelX,}
                },
                StyleMatchRules = new List<StyleMatchRule>()
            {
                new StyleMatchRule() { Key = "highway", Value = "secondary|secondary_link", MatchType = "any" },
                new StyleMatchRule() { Key="footway", Value="sidewalk|crossing", MatchType="not"},
            }},
            new StyleEntry() { MatchOrder = 3300, Name ="tertiaryFilled", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "ffffff", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 98, MaxDrawRes = ConstantValues.zoom10DegPerPixelX / 2},
                    new StylePaint() { HtmlColorCode = "8f8f8f", FillOrStroke = "fill", LineWidthDegrees=0.0000375F, LinePattern= "solid", LayerId = 99, MaxDrawRes = ConstantValues.zoom10DegPerPixelX / 2}
                },
                StyleMatchRules = new List<StyleMatchRule>()
            {
                new StyleMatchRule() { Key = "highway", Value = "tertiary|unclassified|residential|tertiary_link|service|road", MatchType = "any" },
                new StyleMatchRule() { Key="footway", Value="sidewalk|crossing", MatchType="not"},
                new StyleMatchRule() { Key="area", Value="yes", MatchType="equals"}
            }},
            //New in Release 8: Additional styles for more accurate map tiles.
            new StyleEntry() { MatchOrder = 3350, Name ="rail", StyleSet = "mapTiles", //Draw most railways in the same pattern for now.
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "ffffff", FillOrStroke = "fill", LineWidthDegrees=0.00002675F, LinePattern= "40|40", LayerId = 96, MaxDrawRes = ConstantValues.zoom8DegPerPixelX / 2},
                    new StylePaint() { HtmlColorCode = "707070", FillOrStroke = "fill", LineWidthDegrees=0.00003125F, LinePattern= "solid", LayerId = 97, MaxDrawRes = ConstantValues.zoom8DegPerPixelX / 2}
                },
                StyleMatchRules = new List<StyleMatchRule>()
            {
                new StyleMatchRule() { Key = "railway", Value = "rail|construction|disued|narrow_gauge|tram", MatchType = "any" },
            }},
            new StyleEntry() { MatchOrder = 3400, Name ="tree", StyleSet = "mapTiles", //Trees are transparent, so they blend the color of what's under thenm.
                PaintOperations = new List<StylePaint>() { //TODO: consider scaling to 32x32px at zoom 20. Do the math to convert that to degrees.
                    new StylePaint() { HtmlColorCode = "42aedfa3", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 97, MaxDrawRes = ConstantValues.zoom10DegPerPixelX / 2},
                    new StylePaint() { HtmlColorCode = "42a52a2a", FillOrStroke = "fill", LineWidthDegrees=0.000001425F, LinePattern= "solid", LayerId = 96, MaxDrawRes = ConstantValues.zoom10DegPerPixelX / 2},
                },
                StyleMatchRules = new List<StyleMatchRule>()
            {
                new StyleMatchRule() { Key="natural", Value="tree", MatchType="equals"}
            }},
            new StyleEntry() { MatchOrder = 3500, Name ="landfill", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "b6b592", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100, MaxDrawRes = ConstantValues.zoom10DegPerPixelX / 2},
                },
                StyleMatchRules = new List<StyleMatchRule>()
            {
                new StyleMatchRule() { Key="landuse", Value="landfill", MatchType="equals"}
            }},
            new StyleEntry() { MatchOrder = 3600, Name ="farmland", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "eef0d5", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100, MaxDrawRes = ConstantValues.zoom10DegPerPixelX / 2},
                },
                StyleMatchRules = new List<StyleMatchRule>()
            {
                new StyleMatchRule() { Key="landuse", Value="farmland", MatchType="or"},
                new StyleMatchRule() { Key="landuse", Value="greenhouse_horticulture", MatchType="or"},
            }},
            new StyleEntry() { MatchOrder = 3700, Name ="farmyard", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "f5dcba", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100, MaxDrawRes = ConstantValues.zoom10DegPerPixelX / 2},
                },
                StyleMatchRules = new List<StyleMatchRule>()
            {
                new StyleMatchRule() { Key="landuse", Value="farmyard", MatchType="equals"},
            }},
            new StyleEntry() { MatchOrder = 3800, Name ="scrub", StyleSet = "mapTiles", //TODO: import pattern.
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "c8d7ab", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100, MaxDrawRes = ConstantValues.zoom10DegPerPixelX / 2},
                },
                StyleMatchRules = new List<StyleMatchRule>()
            {
                new StyleMatchRule() { Key="natural", Value="scrub", MatchType="equals"},
            }},
            new StyleEntry() { MatchOrder = 3900, Name ="garages", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "dfddce", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100, MaxDrawRes = ConstantValues.zoom10DegPerPixelX / 2},
                },
                StyleMatchRules = new List<StyleMatchRule>()
            {
                new StyleMatchRule() { Key="landuse", Value="garages", MatchType="equals"},
            }},
            new StyleEntry() { MatchOrder = 4000, Name ="military", StyleSet = "mapTiles", //TODO import pattern.
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "ff5555", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100, MaxDrawRes = ConstantValues.zoom10DegPerPixelX / 2},
                },
                StyleMatchRules = new List<StyleMatchRule>()
            {
                new StyleMatchRule() { Key="landuse", Value="military", MatchType="equals"},
            }},
            new StyleEntry() { MatchOrder = 4100, Name ="sportspitch", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "aae0cb", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100, MaxDrawRes = ConstantValues.zoom10DegPerPixelX / 2},
                },
                StyleMatchRules = new List<StyleMatchRule>()
            {
                new StyleMatchRule() { Key="leisure", Value="pitch|track", MatchType="any"},
            }},
            new StyleEntry() { MatchOrder = 4200, Name ="golfgreen", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "aae0cb", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100, MaxDrawRes = ConstantValues.zoom10DegPerPixelX / 2},
                },
                StyleMatchRules = new List<StyleMatchRule>()
            {
                new StyleMatchRule() { Key="golf", Value="green", MatchType="equals"},
            }},
            new StyleEntry() { MatchOrder = 4300, Name ="golfcourse", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "def6c0", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100, MaxDrawRes = ConstantValues.zoom10DegPerPixelX / 2},
                },
                StyleMatchRules = new List<StyleMatchRule>()
            {
                new StyleMatchRule() { Key="leisure", Value="golf_course|miniature_golf", MatchType="any"},
            }},
            new StyleEntry() { MatchOrder = 4400, Name ="stadium", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "dffce2", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100, MaxDrawRes = ConstantValues.zoom10DegPerPixelX / 2},
                },
                StyleMatchRules = new List<StyleMatchRule>()
            {
                new StyleMatchRule() { Key="leisure", Value="sports_centre|stadium", MatchType="or"},
                new StyleMatchRule() { Key="landuse", Value="recreation_ground", MatchType="or"},
            }},
            new StyleEntry() { MatchOrder = 4500, Name ="railway", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "ffc0cb", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100, MaxDrawRes = ConstantValues.zoom10DegPerPixelX / 2},
                },
                StyleMatchRules = new List<StyleMatchRule>()
            {
                new StyleMatchRule() { Key="landuse", Value="railway", MatchType="equals"},
            }},
            new StyleEntry() { MatchOrder = 4600, Name ="airportapron", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "dbdbe1", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100, MaxDrawRes = ConstantValues.zoom10DegPerPixelX / 2},
                },
                StyleMatchRules = new List<StyleMatchRule>()
            {
                new StyleMatchRule() { Key="aeroway", Value="apron", MatchType="equals"},
            }},
            new StyleEntry() { MatchOrder = 4710, Name ="airportrunway", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "bbbbcc", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100, MaxDrawRes = ConstantValues.zoom10DegPerPixelX / 2},
                },
                StyleMatchRules = new List<StyleMatchRule>()
            {
                new StyleMatchRule() { Key="aeroway", Value="runway|taxiway|helipad", MatchType="equals"}, //TODO: helipad is the same color, but has a symbol.
            }},
            new StyleEntry() { MatchOrder = 4730, Name ="airporterminal", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "c5b7ad", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100, MaxDrawRes = ConstantValues.zoom10DegPerPixelX / 2},
                },
                StyleMatchRules = new List<StyleMatchRule>()
            {
                new StyleMatchRule() { Key="aeroway", Value="terminal", MatchType="or"},
                new StyleMatchRule() { Key="building", Value="train_station", MatchType="or"},
                new StyleMatchRule() { Key="aerialway", Value="station", MatchType="or"},
                new StyleMatchRule() { Key="public_transport", Value="station", MatchType="or"},
            }},
            new StyleEntry() { MatchOrder = 4800, Name ="raceway", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "ffc0cb", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100, MaxDrawRes = ConstantValues.zoom10DegPerPixelX / 2},
                },
                StyleMatchRules = new List<StyleMatchRule>()
            {
                new StyleMatchRule() { Key="highway", Value="raceway", MatchType="equals"},
            }},
            
            new StyleEntry() { MatchOrder = 4900, Name ="heath", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "d6d99f", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100, MaxDrawRes = ConstantValues.zoom10DegPerPixelX / 2},
                },
                StyleMatchRules = new List<StyleMatchRule>()
            {
                new StyleMatchRule() { Key="natural", Value="heath", MatchType="equals"},
            }},
            new StyleEntry() { MatchOrder = 5000, Name ="sand", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "f5e9c6", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100, MaxDrawRes = ConstantValues.zoom10DegPerPixelX / 2},
                },
                StyleMatchRules = new List<StyleMatchRule>()
            {
                new StyleMatchRule() { Key="natural", Value="sand", MatchType="or"},
                new StyleMatchRule() { Key="golf", Value="bunker", MatchType="or"},
            }},
            new StyleEntry() { MatchOrder = 5100, Name ="glacier", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "ddecec", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100, MaxDrawRes = ConstantValues.zoom10DegPerPixelX / 2},
                },
                StyleMatchRules = new List<StyleMatchRule>()
            {
                new StyleMatchRule() { Key="natural", Value="glacier", MatchType="equals"},
            }},
            new StyleEntry() { MatchOrder = 5200, Name ="aerodrome", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "e9e7e2", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100, MaxDrawRes = ConstantValues.zoom10DegPerPixelX / 2},
                },
                StyleMatchRules = new List<StyleMatchRule>()
            {
                new StyleMatchRule() { Key="aeroway", Value="aerodrome", MatchType="or"},
                new StyleMatchRule() { Key="amenity", Value="ferry_terminal", MatchType="or"},
                new StyleMatchRule() { Key="amenity", Value="bus_station", MatchType="or"},
            }},
            new StyleEntry() { MatchOrder = 5300, Name ="restarea", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "f9c6c6", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100, MaxDrawRes = ConstantValues.zoom10DegPerPixelX / 2},
                },
                StyleMatchRules = new List<StyleMatchRule>()
                {
                    new StyleMatchRule() { Key="highway", Value="services|rest_area", MatchType="any"},
             }},
            new StyleEntry() { MatchOrder = 5400, Name ="bridge", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "f9c6c6", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 99, MaxDrawRes = ConstantValues.zoom10DegPerPixelX / 2},
                },
                StyleMatchRules = new List<StyleMatchRule>()
                {
                    new StyleMatchRule() { Key="man_made", Value="bridge", MatchType="equals"},
            }},
            new StyleEntry() { MatchOrder = 5500, Name ="power", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "f9c6c6", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100, MaxDrawRes = ConstantValues.zoom10DegPerPixelX / 2},
                },
                StyleMatchRules = new List<StyleMatchRule>()
                {
                    new StyleMatchRule() { Key="power", Value="generator|substation|plant", MatchType="any"},
            }},
            new StyleEntry() { MatchOrder = 5600, Name ="tree_row", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "99add19e", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100, MaxDrawRes = ConstantValues.zoom10DegPerPixelX / 2},
                },
                StyleMatchRules = new List<StyleMatchRule>()
                {
                    new StyleMatchRule() { Key="natural", Value="tree_row", MatchType="equals"},
            }},
            new StyleEntry() { MatchOrder = 5700, Name ="orchard", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "aedfa3", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100, MaxDrawRes = ConstantValues.zoom10DegPerPixelX / 2},
                },
                StyleMatchRules = new List<StyleMatchRule>()
                {
                    new StyleMatchRule() { Key="landuse", Value="orchard|vineyard|plant_nursery", MatchType="any"},
            }},
            new StyleEntry() { MatchOrder = 5800, Name ="allotments", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "c9e1bf", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100, MaxDrawRes = ConstantValues.zoom10DegPerPixelX / 2},
                },
                StyleMatchRules = new List<StyleMatchRule>()
                {
                    new StyleMatchRule() { Key="landuse", Value="allotments", MatchType="any"},
            }},




            //additional areas that I can work out info on go here, though they may not necessarily be the most interesting areas to handle.
            //single color terrains:
            //manmade:clearcut: grass (requires forest to be darker green), but this also isnt mapped on OSMCarto

            //patterns to work in: (colors present)
            //orchard
            //golf rough.
            //military
            //military danger zone


            //NOTE: hiding elements of a given type is done by drawing those elements in a transparent color
            //My default set wants to draw things that haven't yet been identified, so I can see what needs improvement or matched by a rule.
            new StyleEntry() { MatchOrder = 9999, Name ="background",  StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "f2eef9", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 1001 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "*", Value = "*", MatchType = "none" }} },

            new StyleEntry() { MatchOrder = 10000, Name ="unmatched",  StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "00000000", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 1000 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "*", Value = "*", MatchType = "default" }}
            },
        };
            
    }
}
