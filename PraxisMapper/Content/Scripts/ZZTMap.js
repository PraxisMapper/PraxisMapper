//ZZTMap
//The core logic used (with Leaflet) to play games on a map in the browser.

//Things to add here:
//The location logic
//The gameplay loop processing.
//The stock objects

//alert("loaded");

//I need 3 modes: find games, play games, make games.
//The first one is significantly more different from the other 2.
//Find: walk around the map, allow scrolling, find areas outlined in a square to play or edit.
//Play: load game data and handle interactions. Exit back to Find mode when completed or user exits.
//Play should probably operate on one specific map tile zoom size, which might be specially defined.
//Edit: as play, but with the interface to put things on the map yourself and edit their properties. Requires a text editor and keyboard for scripting (means a PC scrollable interface will be required)

//quick prototypes

const fish = {
    name: "fish",
    image: {
        default: "fishFlop.png",
        water: "fishSwim.png",
    },
    playerNear: "script goes here",
};