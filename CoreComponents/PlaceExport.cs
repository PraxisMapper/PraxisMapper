using Microsoft.VisualBasic;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace PraxisCore
{
    /* PlaceExport is the replacement intermediate file format for geomdata/tagsdata.
    Goals:
    - Single file (Zip file, with 1 entry per place, named by sourceitemid-sourceitemtype)
    - Smaller: store geography as binary instead of text (base64 because JSON but still better), compressed.
    - transfer data between databases (no database-specific info or filled-on-demand properties included)
    */

    public class PlaceExport
    {
        public string filename { get;set; }
        public long totalEntries { get; set; }
        ZipArchive zf = null;
        int entryCounter = 0;

        public PlaceExport(string file) {
            filename = file;
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
            if (zf.Entries.Count <= entryCounter)
                return null;

            DbTables.Place place = null;
            using (Stream s = zf.Entries[entryCounter].Open())
            using (StreamReader sr = new StreamReader(s))
            {
                var data = sr.ReadToEnd();
                place = JsonSerializer.Deserialize<DbTables.Place>(data);
            }
            
            entryCounter++;
            return place;
        }

        public DbTables.Place GetSpecificPlace(long sourceItemId, int sourceItemType)
        {
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

        public static void LoadToDatabase(string pmdFile)
        {
            //This is for an existing file that's getting imported into the current DB.
            //EX: if I have pre-processed files available for coastline data, this should just get pulled in as-is.
            //This could be thrown into a folder during load, and when its loading PBFs it could just read pmd's in addition.
            Stopwatch sw = Stopwatch.StartNew();
            Log.WriteLog("Loading " + pmdFile + " to database at " + DateTime.Now);
            var db = new PraxisContext();
            db.ChangeTracker.AutoDetectChangesEnabled = false;

            var entry = new PlaceExport(pmdFile);
            entry.Open();

            int counter = 0;
            DbTables.Place place = entry.GetNextPlace();
            while(place != null) 
            {
                var thisEntry = db.Places.FirstOrDefault(p => p.SourceItemID == place.SourceItemID && p.SourceItemType == place.SourceItemType);
                if (thisEntry == null)
                    db.Places.Add(place);
                else
                {
                    db.Entry(place).CurrentValues.SetValues(thisEntry);
                }
                counter++;
                if (counter % 1000 == 0)
                {
                    db.SaveChanges(); //should probably happen in batches, since we don't know how big a file is.
                    db = new PraxisContext();
                    db.ChangeTracker.AutoDetectChangesEnabled = false;
                    Log.WriteLog("Saved: " + counter + " entries");
                }
                place = entry.GetNextPlace();
            }
            db.SaveChanges(); //should probably happen in batches, since we don't know how big a file is.
            entry.Close();
            sw.Stop();
            Log.WriteLog("Loaded " + entry.filename + " in " + sw.Elapsed);
        }

        public void WriteToDisk()
        {
            Close();
            Open();
        }
    }
}
