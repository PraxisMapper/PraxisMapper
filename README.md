# PraxisMapper
An open-source server for location based games. 

Powered by OpenStreetMap data files.

To focus on the player, and to let them play without tracking them.

Source code for a test application, Hypothesis, is available at https://github.com/drakewill-CRL/Hypothesis


# Requirements
* Visual Studio 2019 16.8 Community (or the mac/linux equivalent)
* .NET 5.0
* SQL Server 2016 or newer OR MariaDB 10.2+ (Required for spatial data types and indexing)


# Features
* Load data in from OpenStreetMap exports
* Create Map Tiles on the fly or ahead of time.
* Supports any sized area.
* Allows for grid-based games to quickly get back info on surroundings
* Server stores as little data as possible about users. 
* Multiple default game modes.
