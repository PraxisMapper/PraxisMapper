# PraxisMapper
An open-source server for location based games. 

Powered by OpenStreetMap data.

To focus on the player, and to let them play without tracking them.

Source code for a test application, Hypothesis, is available at https://github.com/drakewill-CRL/Hypothesis


# Requirements
* Visual Studio 2022 17.0+ Community (or the mac/linux equivalent)
* .NET 6.0
* MariaDB 10.2+ (Required for spatial data types and indexing) OR SQL Server 2016 or newer OR PostgreSQL

# Features
* Simple API handles all the baseline needs for a location based game.
* Load data in from OpenStreetMap exports.
* Create map tiles on demand or ahead of time, from the source map or from gameplay data.
* Draw map tiles in multiple styles.
* Supports any sized area for gameplay. 
* Server stores as little data as possible about users. 
* 3 backend database options: MariaDB is free and simple, PostgreSQL is popular among OpenStreetMap projects, and Microsoft SQL Server for enterprise sized games.

# How to Use PraxisMapper 
* Call MapTile/DrawPlusCode to get a maptile for a gameplay area, or MapTile/DrawPlusCodeCustomElements to draw an overlay with gameplay data from the elements.
* Use Data/GetPlusCode to read info from a grid cell, or Data/SetPlusCode to save data to the server
* Use Data/GetElementData and Data/SetElementData to read and write data based on the items drawn on the map.
* Your game keeps any player location history stored client-side. The server is for interactions, not tracking.


At this time, you are expected to have some programming experience to use PraxisMapper for making games. Building a location-based game with PraxisMapper is not currently suitable as a first coding project.
# Setup Instructions from Source (Windows)
* Download the smallest usable PBF map extract file you can find for the area you want to cover for gameplay from Geofabrik.de
* On OpenStreetMap.org, search for the area you want your game to cover, and write down its Relation ID or Way ID.
* Install MariaDB and create a service account for PraxisMapper
* Update the config files for Larry and PraxisMapper with your connection string for the database, and with the folder path to the PBF file and the specific relation you want to map out (if desired) for Larry.
* Build Larry, then run "Larry -makeServerDb" from the command line.
* Install and configure IIS for ASP.NET Core, creating an application for PraxisMapper
* Publish the PraxisMapper project from VS 2022 and copy the output to your IIS application
