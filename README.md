# PraxisMapper
An open-source server for location based games. Powered by OpenStreetMap data. Follow my on <a rel="me" href="https://mastodon.gamedev.place/@Praxismapper">Mastodon</a>

The fast-setup guide is readable <a href="https://praxismapper.hashnode.dev/setting-up-your-praxismapper-server">here</a>

To focus on the player, and to let them play without tracking them.

Source code for a test application, Hypothesis, is available at https://github.com/PraxisMapper/Hypothesis


# Requirements
* Visual Studio 2022 17.0+ Community (or the mac/linux equivalent)
* .NET 7.0
* [optionally] MariaDB 10.6+ (Recommended) OR SQL Server 2016+ OR PostgreSQL
* System resources may vary with content.
* * Running a county-sized game (500 square miles/1200 square kilometers)? A server can run with as little as 1GB RAM and storage space, with the webserver and DB on the same box.
* * Running a server for a continent? You'll want at least 64GB of RAM on the DB server, and 200GB+ of storage space to handle map data and drawn tiles.

# Features
* Simple API handles all the baseline needs for a location based game. It handles locations, map tiles, and interactions, you can focus on the gameplay.
* Built-in GameTools handle the common tasks you'd expect for a location-based game.
* Load data in from OpenStreetMap exports.
* Create map tiles on demand or ahead of time, from the source map or from gameplay data.
* Draw map tiles in multiple styles, and draw gameplay data to overlay tiles.
* Supports any sized area for gameplay. 
* Server stores as little data as possible about users. 
* 4 backend database options: LocalDB is the automatic, no-config default, MariaDB is free and simpler than other options, Microsoft SQL Server for commercial supported backend, and PostgreSQL is popular among OpenStreetMap projects.

# Performance Examples
* Setting up a county-sized game server (1,200 square miles) takes about 5-30 minutes and can be done with almost no configuration of the server. Disk space use can vary between 200MB in MariaDB with a low density county, to 8GB+ in LocalDB with Los Angeles County.
* Setting up a state-sized game server (53,000 square miles) takes 30+ minutes to process data. You will want to use a full-sized database like MariaDB or SQL Server for this except for the smallest of states.
* Most continents can be converted from source data to working server in under 48 hours of processing time and a little extra planning. Europe requires significantly more space than North America. 

# How to Use PraxisMapper's APIs
* /MapTile handles all the drawing logic for creating baseline map tiles, or overlays to layer multiple tiles together on your client.
* * Call YourServer/MapTile/Area/{PlusCode} to get a maptile for a gameplay area, or MapTile/AreaPlaceData/{PlusCode} to draw an overlay with gameplay data from the elements.
* /Data handles storing and reading data for players, Places, Areas, and global information or settings.
* * Use GET Data/Area/{PlusCode} to read info from a grid cell, or PUT Data/Area/{PlusCode} to save data to the server
* * Use GET Data/Place/{ID} and PUT Data/Place/{ID} to read and write data based on the items drawn on the map.
* /SecureData allows for entries to be encrypted, blocking them from being viewed by unauthorized users.
* * GET and PUT calls both add a password entry
* * SecureData endpoints are appropriate if you want to attach users to places or store location data, so as to not expose it to other players (or the server owner, if the password is provided by the player)
* See the APIDocs.txt file or the wiki tab on GitHub for a full set of API endpoints and expected values.
* More examples are available in Hypothesis, the example mobile client.

At this time, you are expected to have some programming experience to use PraxisMapper for making games. Building a location-based game with PraxisMapper is not currently suitable as a first coding project.
# Minimal Fast-Setup Instructions (Windows)
* Unzip all files from PraxisMapper.zip to C:\Praxis.
* Download the smallest usable PBF map extract file you can find for the area you want to cover for gameplay from Geofabrik.de to C:\Praxis
* On OpenStreetMap.org, search for the area (County or City) you want your game to cover, and write down its Relation ID.
* Open C:\Praxis\appsettings.json, and change the "useRelationForBounds": value from 0 to the Relation ID you wrote down for your County/City
* Run PraxisMapper.exe. It should fire up and start reading data from your PBF file. Some time later, you should get the words "PraxisMapper configured and running." If you go to http://localhost:5000/Server/Test and see "OK", You have a minimum functional PraxisMapper installation.

# Scale Changes
It is entirely feasible for small games to run the entire server on a single PC, with surprisingly low resources. A US county is often an entirely viable space for a local game, particularly in a testing phase of development.
Huge games will require some manual work to set up. Continent-sized servers, or countries that take up most of a continent, will require deleting indexes after creating the database schema, processing and importing data, then re-creating indexes after import in order to remove a few days from that initial load time. This index juggling process is handled with the -makeServerDb command.
