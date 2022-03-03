using NetTopologySuite;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Prepared;
using System.Collections.Generic;
using static PraxisCore.DbTables;

namespace PraxisCore
{
    public static class Singletons
    {
        public static GeometryFactory factory = NtsGeometryServices.Instance.CreateGeometryFactory(4326);
        public static PreparedGeometryFactory pgf = new PreparedGeometryFactory();
        public static bool SimplifyAreas = false;

        /// <summary>
        /// The predefined list of shapes to generate areas in. 4 basic geometric shapes.
        /// </summary>
        public static List<List<Coordinate>> possibleShapes = new List<List<Coordinate>>() //When generating gameplay areas in empty Cell8s
        {
            new List<Coordinate>() { new Coordinate(0, 0), new Coordinate(.5, 1), new Coordinate(1, 0)}, //triangle.
            new List<Coordinate>() { new Coordinate(0, 0), new Coordinate(0, 1), new Coordinate(1, 1), new Coordinate(1, 0) }, //square.
            new List<Coordinate>() { new Coordinate(.2, 0), new Coordinate(0, .8), new Coordinate(.5, 1), new Coordinate(1, .8), new Coordinate(.8, 0) }, //roughly a pentagon.
            new List<Coordinate>() { new Coordinate(.5, 0), new Coordinate(0, .33), new Coordinate(0, .66), new Coordinate(.5, 1), new Coordinate(1, .66), new Coordinate(1, .33) }, //roughly a hexagon.
            //TODO: more shapes, ideally more interesting than simple polygons? Star? Heart? Arc?
        };

        //A * value is a wildcard that means any value counts as a match for that key. If the tag exists, its value is irrelevant. Cannot be used in NOT checks.
        //A * key is a rule that will not match based on tag values, as no key will == *. Used for backgrounds and special styles called up by name.
        //types vary:
        //any: one of the pipe delimited values in the value is present for the given key.
        //equals: the one specific value exists on that key. Slightly faster than Any when matching a single key, but trivially so.
        //or: as long as one of the rules with or is true, accept this entry as a match. each Or entry should be treated as its own 'any' for parsing purposes.
        //not: this rule must be FALSE for the style to be applied.
        //default: always true.
        //New Layer Rules:
        //Roads 90-99
        //default content: 100

        /// <summary>
        /// The baseline set of TagParser styles. 
        /// <list type="bullet">
        /// <item><description>mapTiles: The baseline map tile style, based on OSMCarto</description></item>
        /// <item><description>teamColor: 3 predefined styles to allow for Red(1), Green(2) and Blue(3) teams in a game. Set a tag to the color's ID and then call a DrawCustomX function with this style.</description></item>
        /// <item><description>paintTown: A simple styleset that pulls the color to use from the tag provided.</description></item>
        /// <item><description>adminBounds: Draws only admin boundaries, all levels supported but names match USA's common usage of them.</description></item>
        /// <item><description>outlines: Draws a black border outline for all elements.</description></item>
        /// </list>
        /// </summary>
        public static List<StyleEntry> defaultStyleEntries = new List<StyleEntry>()
        {
            //NOTE: a rough analysis suggests that about 1/3 of entries are 'tertiary' roads and another third are 'building'
            //So moving those 2 entires up to match order 1 and 2 should reduce the amount of time checking through entries in most cases.
            //(Unmatched is 3rd, and that one has to be last by definition.)
            //Roads of varying sizes and colors to match OSM colors
            new StyleEntry() { MatchOrder = 1, Name ="tertiary", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "ffffff", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 98, MaxDrawRes = ConstantValues.zoom10DegPerPixelX / 2},
                    new StylePaint() { HtmlColorCode = "8f8f8f", FillOrStroke = "stroke", LineWidthDegrees=0.0000375F, LinePattern= "solid", LayerId = 99, MaxDrawRes = ConstantValues.zoom10DegPerPixelX / 2}
                },
                StyleMatchRules = new List<StyleMatchRule>()
            {
                new StyleMatchRule() { Key = "highway", Value = "tertiary|unclassified|residential|tertiary_link|service|road", MatchType = "any" },
                new StyleMatchRule() { Key="footway", Value="sidewalk|crossing", MatchType="not"},
            }},
            new StyleEntry() { MatchOrder = 2, Name ="building", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "d9d0c9", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 },
                    new StylePaint() { HtmlColorCode = "B8A89C", FillOrStroke = "stroke", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 99 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "building", Value = "*", MatchType = "equals" }} },
            new StyleEntry() { MatchOrder = 14, Name ="water", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "aad3df", FillOrStroke = "fill", LineWidthDegrees=0.0000625F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "natural", Value = "water|strait|bay|coastline", MatchType = "or"},
                    new StyleMatchRule() {Key = "waterway", Value ="*", MatchType="or" },
                    new StyleMatchRule() {Key = "landuse", Value ="basin", MatchType="or" },
                    new StyleMatchRule() {Key = "leisure", Value ="swimming_pool", MatchType="or" },
                    new StyleMatchRule() {Key = "place", Value ="sea", MatchType="or" }, //stupid Labrador sea value.
                }},
            new StyleEntry() { MatchOrder = 34, Name ="wetland", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "0C4026", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                 StyleMatchRules = new List<StyleMatchRule>() {
                     new StyleMatchRule() { Key = "natural", Value = "wetland", MatchType = "equals" }
                 }},
            new StyleEntry() { IsGameElement = true, MatchOrder = 3, Name ="park", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "C8FACC", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "leisure", Value = "park", MatchType = "or" },
                    new StyleMatchRule() { Key = "leisure", Value = "playground", MatchType = "or" },
            }},
            new StyleEntry() { IsGameElement = true, MatchOrder = 4, Name ="beach", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "fff1ba", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "natural", Value = "beach|shoal", MatchType = "or" },
                    new StyleMatchRule() {Key = "leisure", Value="beach_resort", MatchType="or"}
            } },
            new StyleEntry() { IsGameElement = true, MatchOrder = 5, Name ="university", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "FFFFE5", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "amenity", Value = "university|college", MatchType = "any" }} 
            },
            new StyleEntry() { IsGameElement = true, MatchOrder = 6, Name ="natureReserve", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "124504", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "leisure", Value = "nature_reserve", MatchType = "equals" }} 
            },
            new StyleEntry() {IsGameElement = true, MatchOrder = 7, Name ="cemetery", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "AACBAF", FillOrStroke = "fill", FileName="Landuse-cemetery.png", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "landuse", Value = "cemetery", MatchType = "or" },
                    new StyleMatchRule() {Key="amenity", Value="grave_yard", MatchType="or" } } 
            },
            new StyleEntry() { MatchOrder = 8, Name ="retail", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "FFD4CE", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "landuse", Value = "retail|commercial", MatchType = "or"},
                    new StyleMatchRule() {Key="building", Value="retail|commercial", MatchType="or" },
                    new StyleMatchRule() {Key="shop", Value="*", MatchType="or" }
            }},
            new StyleEntry() { IsGameElement = true, MatchOrder = 9, Name ="tourism", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "660033", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "tourism", Value = "*", MatchType = "equals" }} 
            },
            new StyleEntry() { IsGameElement = true, MatchOrder = 10, Name ="historical", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "B3B3B3", FillOrStroke = "fill", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "historic", Value = "*", MatchType = "equals" }} 
            },
            new StyleEntry() { MatchOrder = 11, Name ="trailFilled", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "F0E68C", FillOrStroke = "fill", LineWidthDegrees=0.000025F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key="highway", Value="path|bridleway|cycleway|footway|living_street", MatchType="any"},
                    new StyleMatchRule() { Key="footway", Value="sidewalk|crossing", MatchType="not"},
                    new StyleMatchRule() { Key="area", Value="yes", MatchType="equals"}
            }},
            new StyleEntry() { IsGameElement = true, MatchOrder = 12, Name ="trail", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "F0E68C", FillOrStroke = "stroke", LineWidthDegrees=0.000025F, LinePattern= "solid", LayerId = 100, MaxDrawRes = ConstantValues.zoom14DegPerPixelX }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key="highway", Value="path|bridleway|cycleway|footway|living_street", MatchType="any"},
                    new StyleMatchRule() { Key="footway", Value="sidewalk|crossing", MatchType="not"}
            }},
            //Admin bounds are transparent on mapTiles, because I would prefer to find and skip them on this style. Use the adminBounds style to draw them on their own tile layer.
            new StyleEntry() { MatchOrder = 13, Name ="admin", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "00FF2020", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "10|5", LayerId = 70 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "boundary", Value = "administrative", MatchType = "equals" }} 
            },
            new StyleEntry() { MatchOrder = 17, Name ="parking", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "EEEEEE", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100, MinDrawRes = ConstantValues.zoom12DegPerPixelX}
                },
                StyleMatchRules = new List<StyleMatchRule>() { 
                    new StyleMatchRule() { Key = "amenity", Value = "parking", MatchType = "equals" }} },

            //New generic entries for mapping by color
            new StyleEntry() { MatchOrder = 18, Name ="greenspace",  StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "cdebb0", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "landuse", Value = "grass|farmland|farmyard|meadow|vineyard|recreation_ground|village_green", MatchType = "or" },
                    new StyleMatchRule() { Key = "natural", Value = "scrub|heath|grassland", MatchType = "or" },
                    new StyleMatchRule() { Key = "leisure", Value = "garden", MatchType = "or" },
            }},
            new StyleEntry() { MatchOrder = 19, Name ="alsobeach",  StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "D7B526", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "natural", Value = "sand|shingle|dune|scree", MatchType = "or" },
                    new StyleMatchRule() { Key = "surface", Value = "sand", MatchType = "or" }
            }},
            new StyleEntry() { MatchOrder = 20, Name ="darkgreenspace",  StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "ADD19E", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "natural", Value = "wood", MatchType = "or" },
                    new StyleMatchRule() { Key = "landuse", Value = "forest|orchard", MatchType = "or" },
            }},
            new StyleEntry() { MatchOrder = 21, Name ="industrial",  StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "EBDBE8", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "landuse", Value = "industrial", MatchType = "equals" },
            }},
            new StyleEntry() { MatchOrder = 22, Name ="residential",  StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "009933", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "landuse", Value = "residential", MatchType = "equals" },
            }},
            new StyleEntry() { MatchOrder = 23, Name ="sidewalk", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "C0C0C0", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "highway", Value = "pedestrian", MatchType = "equals" },
            }},
            //Transparent: we don't usually want to draw census boundaries
            new StyleEntry() { MatchOrder = 24, Name ="censusbounds",  StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "00000000", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "boundary", Value = "census", MatchType = "equals" },
            }},
            //Transparents: Explicitly things that don't help when drawn in one color.
            new StyleEntry() { MatchOrder = 25, Name ="donotdraw",  StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "00000000", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "place", Value = "locality|islet", MatchType = "any" },
            }},
            new StyleEntry() { MatchOrder = 26, Name ="greyFill",  StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "AAAAAA", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "man_made", Value = "breakwater", MatchType = "any" },
            }},

            //Roads of varying sizes and colors to match OSM colors
            new StyleEntry() { MatchOrder = 27, Name ="motorwayFilled", StyleSet = "mapTiles",
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
            new StyleEntry() { MatchOrder = 28, Name ="motorway", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "e892a2", FillOrStroke = "fill", LineWidthDegrees=0.000125F, LinePattern= "solid", LayerId = 92},
                    new StylePaint() { HtmlColorCode = "dc2a67", FillOrStroke = "fill", LineWidthDegrees=0.000155F, LinePattern= "solid", LayerId = 93}
                },
                StyleMatchRules = new List<StyleMatchRule>()
            {
                new StyleMatchRule() { Key = "highway", Value = "motorway|trunk|motorway_link|trunk_link", MatchType = "any" },
                new StyleMatchRule() { Key="footway", Value="sidewalk|crossing", MatchType="not"},
            }},
            new StyleEntry() { MatchOrder = 29, Name ="primaryFilled", StyleSet = "mapTiles",
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
            //Roads of varying sizes and colors to match OSM colors
            new StyleEntry() { MatchOrder = 30, Name ="primary", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "fcd6a4", FillOrStroke = "stroke", LineWidthDegrees=0.00005F, LinePattern= "solid", LayerId = 94, MaxDrawRes = ConstantValues.zoom6DegPerPixelX /2 },
                    new StylePaint() { HtmlColorCode = "a06b00", FillOrStroke = "stroke", LineWidthDegrees=0.000085F, LinePattern= "solid", LayerId = 95, MaxDrawRes = ConstantValues.zoom6DegPerPixelX /2}
                },
                StyleMatchRules = new List<StyleMatchRule>()
            {
                new StyleMatchRule() { Key = "highway", Value = "primary|primary_link", MatchType = "any" },
                new StyleMatchRule() { Key="footway", Value="sidewalk|crossing", MatchType="not"},
            }},
            new StyleEntry() { MatchOrder = 31, Name ="secondaryFilled",  StyleSet = "mapTiles",
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
            //Roads of varying sizes and colors to match OSM colors
            new StyleEntry() { MatchOrder = 32, Name ="secondary",  StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "f7fabf", FillOrStroke = "stroke", LineWidthDegrees=0.0000375F, LinePattern= "solid", LayerId = 96, MaxDrawRes = ConstantValues.zoom8DegPerPixelX,},
                    new StylePaint() { HtmlColorCode = "707d05", FillOrStroke = "stroke", LineWidthDegrees=0.0000625F, LinePattern= "solid", LayerId = 97, MaxDrawRes = ConstantValues.zoom8DegPerPixelX,}
                },
                StyleMatchRules = new List<StyleMatchRule>()
            {
                new StyleMatchRule() { Key = "highway", Value = "secondary|secondary_link", MatchType = "any" },
                new StyleMatchRule() { Key="footway", Value="sidewalk|crossing", MatchType="not"},
            }},
            new StyleEntry() { MatchOrder = 33, Name ="tertiaryFilled", StyleSet = "mapTiles",
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

            //additional areas that I can work out info on go here, though they may not necessarily be the most interesting areas to handle.
            //single color terrains:
            //Aeroway:apron: dbdbe1
            //aeroway:runway: bbbbcc
            //aeroway:taxiway: bbbbcc
            //aeroway:terminal: c5b7ad
            //highway:raceway: ffc0cb (might also be a line)
            //landuse:farmland: eef0d5 (currently a subtype of Greenspace) (also landuse:greenhouse_horticulture)
            //landuse:farmyard: f5dcba (currently a subtype of Greenspace)
            //landuse:garages dfddce
            //@scrub: #c8d7ab;  (currently Greenspace)
            //@orchard: #aedfa3; // also vineyard, plant_nursery
            //@military: #f55;
            //@place_of_worship: #d0d0d0; // also landuse_religious
            //@pitch: #aae0cb;    sports pitch/track, also golf_green
            //@campsite: #def6c0; // also caravan_site, picnic_site, golf_course
            //most golf features are 'grass' by color.
            //landuse:landfill  b6b592
            //landuse:railway ebdbe8 (same color as industrial, space people shouldnt be in.)
            //bridges are 'black' but thats not #000000 on the actual rendering.
            //manmade:clearcut: grass (requires forest to be darker green)
            //allotments: c9e1bf
            //natural:tree : greenspace color, but a single marker, 30x30pc at zoom 20 (TODO: calc that to my lineWidth value in lat/lng degrees.


            //line info:


            //NOTE: hiding elements of a given type is done by drawing those elements in a transparent color
            //My default set wants to draw things that haven't yet been identified, so I can see what needs improvement or matched by a rule.
            new StyleEntry() { MatchOrder = 9999, Name ="background",  StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "F2EFE9", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 101 }
                },
                StyleMatchRules = new List<StyleMatchRule>() { 
                    new StyleMatchRule() { Key = "*", Value = "*", MatchType = "none" }} },

            new StyleEntry() { MatchOrder = 10000, Name ="unmatched",  StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "00000000", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() { 
                    new StyleMatchRule() { Key = "*", Value = "*", MatchType = "default" }} 
            },

            //TODO: these need their sizes adjusted to be wider. Right now, in degrees, the overlap is nearly invisible unless you're at zoom 20.
            //More specific admin-bounds tags, named matching USA values for now.
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
        
            //Team Colors now part of the same default list.
            new StyleEntry() { MatchOrder = 1, Name ="1",  StyleSet = "teamColor",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "88FF0000", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 99 },
                    new StylePaint() { HtmlColorCode = "FF0000", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "team", Value = "red", MatchType = "equals"},
            }},
            new StyleEntry() { MatchOrder = 2, Name ="2",   StyleSet = "teamColor",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "8800FF00", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 99 },
                    new StylePaint() { HtmlColorCode = "00FF00", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "team", Value = "green", MatchType = "equals"},
            }},
            new StyleEntry() { MatchOrder = 3, Name ="3",  StyleSet = "teamColor",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "880000FF", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 99 },
                    new StylePaint() { HtmlColorCode = "0000FF", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "team", Value = "blue", MatchType = "equals"},
            }},

            //background is a mandatory style entry name, but its transparent here..
            new StyleEntry() { MatchOrder = 10000, Name ="background",  StyleSet = "teamColor",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "00000000", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 101 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "*", Value = "*", MatchType = "default"},
            }},

            //Paint the Town special style.
            new StyleEntry() { MatchOrder = 1, Name ="tag",  StyleSet = "paintTown",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "01000000", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100, FromTag=true }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "*", Value = "*", MatchType = "default"},
            }},
            //background is a mandatory style entry name, but its transparent here..
            new StyleEntry() { MatchOrder = 10000, Name ="background",  StyleSet = "paintTown",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "00000000", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 101 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "*", Value = "*", MatchType = "default"},
            }},

            //new style to allow only outlines of ALL entries.
            new StyleEntry() { MatchOrder = 1, Name ="1",  StyleSet = "outlines",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "000000", FillOrStroke = "stroke", LineWidthDegrees=0.0000125F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "*", Value = "*", MatchType = "default"},
            }},
            //background is a mandatory style entry name, but its transparent here..
            new StyleEntry() { MatchOrder = 10000, Name ="background",  StyleSet = "outlines",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "00000000", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 101 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key = "bg", Value = "bg", MatchType = "equals"}, //this one only gets called by name anyways.
            }},
            //this name needs to exist because of the default style using this name.
            new StyleEntry() { MatchOrder = 10001, Name ="unmatched",  StyleSet = "outlines",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "00000000", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100 }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "a", Value = "s", MatchType = "equals" }}
            },

        };

        //Short counter by type. Ideally would like 4+ of each type, whatever types may end up being.
        //Using the type data from the original source for now.
        //Short rules:
        //Normal: any otherwise untagged area
        //water: around water areas.
        //ghost: graveyards.
        //Grass: parks.

        //grass 3
        //normal 3
        //flying 1
        //water 5
        //poison 2
        //electric 1
        //dark 3
        //bug 5
        //ghost 2
        //fairy 4
        //Fire 3
        //ground 2
        //psychic 3
        //rock 2
        //dragon 1
        //ice 1
        //fighting 1



        //26 total entries for Hypothesis/example usage.
        //public static List<Creature> defaultCreatures = new List<Creature>() {
        //    new Creature() { name ="Acafia", type1 ="Grass", type2 = "", imageName ="CreatureImages/acafia.png" },
        //    new Creature() { name ="Acceleret", type1 ="Normal", type2 = "Flying", imageName ="CreatureImages/acceleret.png" },
        //    new Creature() { name ="Aeolagio", type1 ="Water", type2 = "Poison", imageName ="CreatureImages/aeolagio.png" },
        //    new Creature() { name ="Bandibat", type1 ="Electric", type2 = "Dark", imageName ="CreatureImages/bandibat.png" },
        //    new Creature() { name ="Belamrine", type1 ="Bug", type2 = "Water", imageName ="CreatureImages/belmarine.png" },
        //    new Creature() { name ="Bojina", type1 ="Ghost", type2 = "", imageName ="CreatureImages/bojina.png" },
        //    new Creature() { name ="Caslot", type1 ="Dark", type2 = "Fairy", imageName ="CreatureImages/caslot.png" },
        //    new Creature() { name ="Cindigre", type1 ="Fire", type2 = "", imageName ="CreatureImages/cindigre.png" },
        //    new Creature() { name ="Curlsa", type1 ="Fairy", type2 = "", imageName ="CreatureImages/curlsa.png" },
        //    new Creature() { name ="Decicorn", type1 ="Poison", type2 = "", imageName ="CreatureImages/decicorn.png" },
        //    new Creature() { name ="Dauvespa", type1 ="Bug", type2 = "Ground", imageName ="CreatureImages/dauvespa.png" },
        //    new Creature() { name ="Drakella", type1 ="Water", type2 = "Grass", imageName ="CreatureImages/drakella.png" },
        //    new Creature() { name ="Eidograph", type1 ="Ghost", type2 = "Psychic", imageName ="CreatureImages/eidograph.png" },
        //    new Creature() { name ="Encanoto", type1 ="Psychic", type2 = "", imageName ="CreatureImages/encanoto.png" },
        //    new Creature() { name ="Faintrick", type1 ="Normal", type2 = "", imageName ="CreatureImages/faintrick.png" },
        //    new Creature() { name ="Galavena", type1 ="Rock", type2 = "Psychic", imageName ="CreatureImages/galavena.png" },
        //    new Creature() { name ="Vanitarch", type1 ="Bug", type2 = "Fairy", imageName ="CreatureImages/vanitarch.png" },
        //    new Creature() { name ="Grotuille", type1 ="Water", type2 = "Rock", imageName ="CreatureImages/grotuille.png" },
        //    new Creature() { name ="Gumbwaal", type1 ="Normal", type2 = "", imageName ="CreatureImages/gumbwaal.png" },
        //    new Creature() { name ="Mandragoon", type1 ="Grass", type2 = "Dragon", imageName ="CreatureImages/mandragoon.png" },
        //    new Creature() { name ="Ibazel", type1 ="Dark", type2 = "", imageName ="CreatureImages/ibazel.png" },
        //    new Creature() { name ="Makappa", type1 ="Ice", type2 = "Fire", imageName ="CreatureImages/makappa.png" },
        //    new Creature() { name ="Pyrobin", type1 ="Fire", type2 = "Fairy", imageName ="CreatureImages/pyrobin.png" },
        //    new Creature() { name ="Rocklantis", type1 ="Water", type2 = "Fighting", imageName ="CreatureImages/rocklantis.png" },
        //    new Creature() { name ="Strixlan", type1 ="Dark", type2 = "Flying", imageName ="CreatureImages/strixlan.png" },
        //    new Creature() { name ="Tinimer", type1 ="Bug", type2 = "", imageName ="CreatureImages/tinimer.png" },
        //    new Creature() { name ="Vaquerado", type1 ="Bug", type2 = "Ground", imageName ="CreatureImages/vaquerado.png" },
        //};
    }
}
