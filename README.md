# PraxisMapper
An open-source server for location based games. 

Powered by OpenStreetMap data files.

To focus on the player, and to let them play without tracking them.


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

# Compilation
A pre-built apk for Hypothesis should be available in the Google Play Store for approx. $3.
If you want to build your own copy, or use the app as a baseline:
* Install Solar2D (https://solar2d.com/)
* Check out or download the code from this repo
* Open Corona Simulator in Solar2D, open the folder with the code you downloaded
* Click File/Build/Android (or iOS)
* Apply the settings and certificates as necessary for your build target.

