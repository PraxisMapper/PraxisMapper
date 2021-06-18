using System.Collections.Generic;
using System.IO;
using System.Linq;

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
