using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;

namespace PraxisCore
{
    /* PlaceExport is the replacement intermediate file format for geomdata/tagsdata.
    Goals:
    - Single file (Zip file, with 1 entry per place, named by sourceitemid-sourceitemtype)
    - Smaller: store geography as binary instead of text (base64 because JSON but still better), compressed.
    - transfer data between databases (no database-specific info or filled-on-demand properties included)

    Missing features vs PBF:
    - Batch loading for speed (Every write has a read command attached right now, should do that all at once)
    */

    public class PlaceExport
    {
        public string filename { get; set; }
        public long totalEntries { get; set; }
        ZipArchive zf = null;
        int entryCounter = 0;
        Envelope bounds = null;
        public string processingMode = "normal";
        public string styleSet = "importAll";

        public PlaceExport(string file)
        {
            filename = file;
            Open();
        }

        public void Open()
        {
            if (!File.Exists(filename))
                using (var newFile = new FileStream(filename, FileMode.CreateNew))
                {
                    zf = new ZipArchive(newFile, ZipArchiveMode.Create); //this should make and close the zip file.
                    zf.Dispose();
                }

            zf = ZipFile.Open(filename, ZipArchiveMode.Update);
            totalEntries = zf.Entries.Count;
        }

        public void Close()
        {
            zf.Dispose();
            if (totalEntries == 0)
            {
                File.Delete(filename);
            }
        }

        /// <summary>
        /// Searches the PMD file for a Place with the given ID, and uses it as the bounds when reading files. Item must fall within the Place's envelope to be loaded.
        /// </summary>
        /// <param name="elementId"></param>
        public void SetBoundary(long elementId)
        {
            var boundEntry = GetSpecificPlace(elementId, 3);
            if (boundEntry == null)
                boundEntry = GetSpecificPlace(elementId, 2);

            if (boundEntry != null)
                bounds = boundEntry.ElementGeometry.EnvelopeInternal;
        }

        /// <summary>
        /// Sets the bounds for loading Places to the given envelope. Use when the server bounds are not an item inside the PMD file. Item must fall within the Place's envelope to be loaded.
        /// </summary>
        /// <param name="boundary"></param>
        public void SetBoundary(Envelope boundary)
        {
            bounds = boundary;
        }

        public void SkipTo(int skip)
        {
            entryCounter = skip;
        }

        public void AddEntry(DbTables.Place p)
        {
            //NOTE: the simple way of updating entries (write new data to stream) doesnt work on its own.
            var data = JsonSerializer.Serialize(p);
            var entry = zf.GetEntry(p.SourceItemID + "-" + p.SourceItemType);
            if (entry != null)
                entry.Delete();
            else
                totalEntries++;

            entry = zf.CreateEntry(p.SourceItemID + "-" + p.SourceItemType, CompressionLevel.SmallestSize);

            using (var entryStream = entry.Open())
            {
                using (var streamWriter = new StreamWriter(entryStream))
                    streamWriter.Write(data);
            }
        }

        public DbTables.Place GetNextPlace()
        {
            string data = null;
            DbTables.Place place = null;
            while (place == null)
            {
                if (zf.Entries.Count <= entryCounter)
                    return null;

                using (Stream s = zf.Entries[entryCounter].Open())
                using (StreamReader sr = new StreamReader(s))
                {
                    data = sr.ReadToEnd();
                    place = JsonSerializer.Deserialize<DbTables.Place>(data);
                    entryCounter++;
                    if (bounds == null || bounds.Intersects(place.ElementGeometry.EnvelopeInternal))
                    {
                        //Place.PreTag(place); //This is done automatically by the deserializer.
                        if (styleSet != "importAll" && !place.PlaceData.Any(d => d.DataKey == styleSet))
                        {
                            place = null;
                            continue;
                        }

                        if (processingMode == "center")
                            place.ElementGeometry = place.ElementGeometry.Centroid;
                        else if (processingMode == "expandPoints" && place.SourceItemType == 1)
                        {
                            place.ElementGeometry = place.ElementGeometry.Buffer(ConstantValues.resolutionCell8);
                        }
                        else if (processingMode == "minimize")
                        {
                            place.ElementGeometry = Singletons.reducer.Reduce(NetTopologySuite.Simplify.TopologyPreservingSimplifier.Simplify(place.ElementGeometry, ConstantValues.resolutionCell10).Fix());
                        }
                    }
                    else
                        place = null;
                }
            }

            return place;
        }

        public List<DbTables.Place> GetNextPlaces(int count)
        {
            List<DbTables.Place> results = new List<DbTables.Place>(count);
            for (int i = 0; i < count; i++)
            {
                var place = GetNextPlace();
                if (place != null)
                    results.Add(place);
            }

            return results;
        }

        public DbTables.Place GetSpecificPlace(long sourceItemId, int sourceItemType)
        {
            //Intentionally ignoring bounds check here, since the user is specifically asking for this one item.
            DbTables.Place place = null;
            var entry = zf.GetEntry(sourceItemId.ToString() + "-" + sourceItemType.ToString());
            if (entry == null)
                return null;

            using (Stream s = entry.Open())
            using (StreamReader sr = new StreamReader(s))
            {
                var data = sr.ReadToEnd();
                place = JsonSerializer.Deserialize<DbTables.Place>(data);
            }

            return place;
        }

        void SaveProgress()
        {
            File.WriteAllText(filename + ".progress", entryCounter.ToString());
        }

        void LoadProgress()
        {
            if (File.Exists(filename + ".progress"))
                entryCounter = File.ReadAllText(filename + ".progress").ToInt();
        }

        void DeleteProgressFile()
        {
            if (File.Exists(filename + ".progress"))
                File.Delete(filename + ".progress");
        }

        public static void LoadToDatabase(string pmdFile, string processingMode = "normal", Envelope bounds = null)
        {
            //This is for an existing file that's getting imported into the current DB.
            //EX: if I have pre-processed files available for coastline data, this should just get pulled in as-is.
            //This could be thrown into a folder during load, and when its loading PBFs it could just read pmd's in addition.

            Stopwatch sw = Stopwatch.StartNew();
            Log.WriteLog("Loading " + pmdFile + " to database at " + DateTime.Now);
            var db = new PraxisContext();
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            db.Database.SetCommandTimeout(300);
            int batchSize = 10000; //10,000 should be small enough for most block-as-file imports, but the ocean data file is larger than that, and files could be merged.

            var entry = new PlaceExport(pmdFile);
            entry.bounds = bounds;
            entry.processingMode = processingMode;
            entry.LoadProgress();

            //Batching this is a lot harder because I can't guarentee that all of these are the same item type.
            //var batchSize = 100;
            //var places = entry.GetNextPlaces(batchSize);
            //var ids = places.Select(p => p.SourceItemID).ToList();
            //var entries = db.Places.Include(p => p.Tags).Include(p => p.PlaceData).Where(p => ids.Contains(p.SourceItemID)).ToDictionary(k => k.SourceItemId, v => v);

            long placeCounter = 0;
            long entryCounter = 0;
            DbTables.Place place = entry.GetNextPlace();
            while (place != null)
            {
                //TODO: batch this work so we can do 1 DB call for batchSize reads. Will dramatically improve speed.
                //var thisEntry = db.Places.Include(p => p.Tags).Include(p => p.PlaceData).FirstOrDefault(p => p.SourceItemID == place.SourceItemID && p.SourceItemType == place.SourceItemType);
                if (true) // (thisEntry == null)
                    db.Places.Add(place);
                else
                {
                    //Place.UpdateChanges(thisEntry, place, db);
                }
                placeCounter++;
                if (placeCounter % batchSize == 0)
                {
                    entryCounter += db.SaveChanges(); //should probably happen in batches, since we don't know how big a file is.
                    db.ChangeTracker.Clear();
                    entry.SaveProgress();
                    Log.WriteLog("Saved: " + entryCounter + " total entries");
                    db.ChangeTracker.Clear();
                }
                place = entry.GetNextPlace();
            }
            db.SaveChanges();
            entry.DeleteProgressFile();
            entry.Close();
            sw.Stop();
            Log.WriteLog("Loaded " + entry.filename + " in " + sw.Elapsed);
        }

        public void WriteToDisk()
        {
            Close();
            Open();
        }

        public static void WritePlacesToFile(string filename, List<DbTables.Place> places)
        {
            var pe = new PlaceExport(filename);
            foreach (var place in places)
                pe.AddEntry(place);

            try
            {
                pe.Close();
            }
            catch (Exception ex)
            {
                Log.WriteLog("Error writing data to disk:" + ex.Message, Log.VerbosityLevels.Errors);
            }
        }
    }
}
