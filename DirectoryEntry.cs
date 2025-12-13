using System;
using System.Text;

namespace MiniFatFs
{
    public class DirectoryEntry
    {
        public string Name { get; set; }        // 8.3 uppercase
        public byte Attribute { get; set; }     // 0x10 = folder, 0x20 = file
        public int FirstCluster { get; set; }   // رقم أول cluster
        public int FileSize { get; set; }       // حجم الملف بالبايت

        public DirectoryEntry(string name, byte attr, int firstCluster, int fileSize)
        {
            Name = FormatNameTo8Dot3(name);
            Attribute = attr;
            FirstCluster = firstCluster;
            FileSize = fileSize;
        }

        public static string FormatNameTo8Dot3(string name)
        {
            name = name.ToUpper();
            var parts = name.Split('.');
            string fname = parts[0].PadRight(8).Substring(0, 8);
            string ext = parts.Length > 1 ? parts[1].PadRight(3).Substring(0, 3) : "   ";
            return fname + ext;
        }

        public static string Parse8Dot3Name(string rawName)
        {
            string fname = rawName.Substring(0, 8).Trim();
            string ext = rawName.Substring(8, 3).Trim();
            return ext.Length > 0 ? $"{fname}.{ext}" : fname;
        }
    }
}