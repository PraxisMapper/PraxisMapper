using CoreComponents;
using CoreComponents.Support;
using NetTopologySuite.Geometries;
using OsmSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using static CoreComponents.DbTables;
using static CoreComponents.Singletons;
using static Larry.PbfOperations;

namespace Larry
{
    //FileCommands is intended for functions that do some work on various file types. Processing map data from PBFs belongs to PbfOperations.
    public static class FileCommands
    {
        public static void ResetFiles(string folder)
        {
            List<string> filenames = System.IO.Directory.EnumerateFiles(folder, "*.*Done").ToList();
            foreach (var file in filenames)
            {
                File.Move(file, file.Substring(0, file.Length - 4));
            }
        }
    }
}
