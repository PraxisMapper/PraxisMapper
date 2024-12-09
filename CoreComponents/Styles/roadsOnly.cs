using System.Collections.Generic;
using static PraxisCore.DbTables;

namespace PraxisCore.Styles
{
    /// <summary>
    /// A style that only draws roads. Used for an experiment, keeping in case I need it again for something.
    /// </summary>
    public static class roadsOnly
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
            new StyleEntry() { IsGameElement = true, MatchOrder = 150, Name ="trail", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "F0E68C", FillOrStroke = "stroke", LineWidthDegrees=0.000025F, LinePattern= "solid", LayerId = 100, MaxDrawRes = ConstantValues.zoom14DegPerPixelX }
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() {Key="highway", Value="path|bridleway|cycleway|footway|living_street", MatchType="any"},
                    new StyleMatchRule() { Key="footway", Value="sidewalk|crossing", MatchType="not"}
            }},
            new StyleEntry() { MatchOrder = 170, Name ="parking", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "EEEEEE", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 100, MinDrawRes = ConstantValues.zoom12DegPerPixelX}
                },
                StyleMatchRules = new List<StyleMatchRule>() {
                    new StyleMatchRule() { Key = "amenity", Value = "parking", MatchType = "equals" }}
            },

            //Roads of varying sizes and colors to match OSM colors
            new StyleEntry() { MatchOrder = 270, Name ="motorwayFilled", StyleSet = "mapTiles",
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
            new StyleEntry() { MatchOrder = 280, Name ="motorway", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "e892a2", FillOrStroke = "fill", LineWidthDegrees=0.000125F, LinePattern= "solid", LayerId = 92},
                    new StylePaint() { HtmlColorCode = "dc2a67", FillOrStroke = "fill", LineWidthDegrees=0.000155F, LinePattern= "solid", LayerId = 93}
                },
                StyleMatchRules = new List<StyleMatchRule>()
            {
                new StyleMatchRule() { Key = "highway", Value = "motorway|trunk|motorway_link|trunk_link", MatchType = "any" },
                new StyleMatchRule() { Key="footway", Value="sidewalk|crossing", MatchType="not"},
            }},
            new StyleEntry() { MatchOrder = 290, Name ="primaryFilled", StyleSet = "mapTiles",
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
            new StyleEntry() { MatchOrder = 300, Name ="primary", StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "fcd6a4", FillOrStroke = "stroke", LineWidthDegrees=0.00005F, LinePattern= "solid", LayerId = 94, MaxDrawRes = ConstantValues.zoom6DegPerPixelX /2 },
                    new StylePaint() { HtmlColorCode = "a06b00", FillOrStroke = "stroke", LineWidthDegrees=0.000085F, LinePattern= "solid", LayerId = 95, MaxDrawRes = ConstantValues.zoom6DegPerPixelX /2}
                },
                StyleMatchRules = new List<StyleMatchRule>()
            {
                new StyleMatchRule() { Key = "highway", Value = "primary|primary_link", MatchType = "any" },
                new StyleMatchRule() { Key="footway", Value="sidewalk|crossing", MatchType="not"},
            }},
            new StyleEntry() { MatchOrder = 310, Name ="secondaryFilled",  StyleSet = "mapTiles",
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
            new StyleEntry() { MatchOrder = 320, Name ="secondary",  StyleSet = "mapTiles",
                PaintOperations = new List<StylePaint>() {
                    new StylePaint() { HtmlColorCode = "f7fabf", FillOrStroke = "stroke", LineWidthDegrees=0.0000375F, LinePattern= "solid", LayerId = 96, MaxDrawRes = ConstantValues.zoom8DegPerPixelX,},
                    new StylePaint() { HtmlColorCode = "707d05", FillOrStroke = "stroke", LineWidthDegrees=0.0000625F, LinePattern= "solid", LayerId = 97, MaxDrawRes = ConstantValues.zoom8DegPerPixelX,}
                },
                StyleMatchRules = new List<StyleMatchRule>()
            {
                new StyleMatchRule() { Key = "highway", Value = "secondary|secondary_link", MatchType = "any" },
                new StyleMatchRule() { Key="footway", Value="sidewalk|crossing", MatchType="not"},
            }},
            new StyleEntry() { MatchOrder = 330, Name ="tertiaryFilled", StyleSet = "mapTiles",
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
                    new StylePaint() { HtmlColorCode = "000000", FillOrStroke = "fill", LineWidthDegrees=0.00000625F, LinePattern= "solid", LayerId = 1001 }
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
