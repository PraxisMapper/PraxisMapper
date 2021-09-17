# PraxisMapper
An open-source server for location based games. 

Powered by OpenStreetMap data files.

To focus on the player, and to let them play without tracking them.

Source code for a test application, Hypothesis, is available at https://github.com/drakewill-CRL/Hypothesis


# Requirements
* Visual Studio 2022 17.0+ Community (or the mac/linux equivalent)
* .NET 6.0
* SQL Server 2016 or newer OR MariaDB 10.2+ (Required for spatial data types and indexing) OR PostgreSQL


# Features
* Load data in from OpenStreetMap exports
* Create Map Tiles on the fly or ahead of time.
* Supports any sized area.
* Allows for grid-based games to quickly get back info on surroundings
* Server stores as little data as possible about users. 
* Multiple default game modes.

# Setup Instructions (Windows)
* Download the smallest usable PBF map extract file you can find for the area you want to cover for gameplay from Geofabrik.de
* On OpenStreetMap.org, search for the area you want your game to cover, and write down its Relation ID or Way ID.
* Install MariaDB and create a service account for PraxisMapper
* Update the config files for Larry and PraxisMapper with your connection string for the database, and with the folder path to the PBF file for Larry
* Build and run Larry, then execute it from the command line with your extract filename and Relation/Way ID, like "Larry -createDB -loadOneArea:YourExtractFile.osm.pbf:123456"
* Install and configure IIS for ASP.NET Core, creating an application for PraxisMapper
* Publish the PraxisMapper project from VS 2022 and copy the output to your IIS application
