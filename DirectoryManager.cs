using System;
using System.Collections.Generic;

namespace MiniFatFs
{
    public class DirectoryManager
    {
        private readonly VirtualDisk _disk;
        private readonly FatTableManager _fat;

        private const int ENTRY_SIZE = 32; 

        public DirectoryManager(VirtualDisk disk, FatTableManager fatManager)
        {
            _disk = disk ?? throw new ArgumentNullException(nameof(disk));
            _fat = fatManager ?? throw new ArgumentNullException(nameof(fatManager));
        }

        public List<DirectoryEntry> ReadDirectory(int startCluster)
        {
            List<DirectoryEntry> entries = new List<DirectoryEntry>();
            List<int> clusters = _fat.FollowChain(startCluster);

            foreach (var clusterNum in clusters)
            {
                byte[] cluster = _disk.ReadCluster(clusterNum);
                for (int i = 0; i < cluster.Length; i += ENTRY_SIZE)
                {
                    byte firstByte = cluster[i];
                    if (firstByte == 0x00) continue;

                    byte[] nameBytes = new byte[11];
                    Array.Copy(cluster, i, nameBytes, 0, 11);
                    string name = Converter.BytesToString(nameBytes, 11);
                    
                    byte attr = cluster[i + 11];
                    int firstCluster = BitConverter.ToInt32(cluster, i + 20);
                    int fileSize = BitConverter.ToInt32(cluster, i + 28);

                    entries.Add(new DirectoryEntry(name, attr, firstCluster, fileSize));
                }
            }

            return entries;
        }

        public DirectoryEntry FindDirectoryEntry(int startCluster, string name)
        {
            string formattedName = DirectoryEntry.FormatNameTo8Dot3(name);
            List<int> clusters = _fat.FollowChain(startCluster);

            foreach (var clusterNum in clusters)
            {
                byte[] cluster = _disk.ReadCluster(clusterNum);
                for (int i = 0; i < cluster.Length; i += ENTRY_SIZE)
                {
                    byte firstByte = cluster[i];
                    if (firstByte == 0x00) continue;

                    byte[] nameBytes = new byte[11];
                    Array.Copy(cluster, i, nameBytes, 0, 11);
                    string entryName = Converter.BytesToString(nameBytes, 11);
                    
                    if (entryName.Equals(formattedName, StringComparison.OrdinalIgnoreCase))
                    {
                        byte attr = cluster[i + 11];
                        int firstCluster = BitConverter.ToInt32(cluster, i + 20);
                        int fileSize = BitConverter.ToInt32(cluster, i + 28);
                        return new DirectoryEntry(entryName, attr, firstCluster, fileSize);
                    }
                }
            }
            return null;
        }

        public void AddDirectoryEntry(int startCluster, DirectoryEntry newEntry)
        {
            List<int> clusters = _fat.FollowChain(startCluster);

            foreach (var clusterNum in clusters)
            {
                byte[] cluster = _disk.ReadCluster(clusterNum);

                for (int i = 0; i < cluster.Length; i += ENTRY_SIZE)
                {
                    if (cluster[i] == 0x00)
                    {
                        WriteEntryToCluster(cluster, i, newEntry);
                        _disk.WriteCluster(clusterNum, cluster);
                        return;
                    }
                }
            }

            int newCluster = _fat.AllocateChain(1);
            byte[] newClusterData = new byte[FsConstants.CLUSTER_SIZE];
            WriteEntryToCluster(newClusterData, 0, newEntry);
            _disk.WriteCluster(newCluster, newClusterData);
            
            if (clusters.Count > 0)
            {
                _fat.SetFatEntry(clusters[clusters.Count - 1], newCluster);
            }
            _fat.FlushFatToDisk();
        }

        public void RemoveDirectoryEntry(int startCluster, DirectoryEntry entry)
        {
            List<int> clusters = _fat.FollowChain(startCluster);

            foreach (var clusterNum in clusters)
            {
                byte[] cluster = _disk.ReadCluster(clusterNum);

                for (int i = 0; i < cluster.Length; i += ENTRY_SIZE)
                {
                    byte[] nameBytes = new byte[11];
                    Array.Copy(cluster, i, nameBytes, 0, 11);
                    string name = Converter.BytesToString(nameBytes, 11);
                    
                    if (name.Equals(entry.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        cluster[i] = 0x00;
                        _disk.WriteCluster(clusterNum, cluster);
                        _fat.FreeChain(entry.FirstCluster);
                        _fat.FlushFatToDisk();
                        return;
                    }
                }
            }
        }

        private void WriteEntryToCluster(byte[] cluster, int offset, DirectoryEntry entry)
        {
            byte[] nameBytes = Converter.StringToBytes(entry.Name);
            Array.Copy(nameBytes, 0, cluster, offset, 11);
            cluster[offset + 11] = entry.Attribute;
            Array.Copy(BitConverter.GetBytes(entry.FirstCluster), 0, cluster, offset + 20, 4);
            Array.Copy(BitConverter.GetBytes(entry.FileSize), 0, cluster, offset + 28, 4);
        }
    }
}