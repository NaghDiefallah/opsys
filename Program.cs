using System;
using MiniFatFs;

namespace MiniFatFs;

class Program
{
    static void Main(string[] args)
    {
        string diskPath = "fatty.bin";
        var disk = new VirtualDisk();

        try
        {
            disk.Initialize(diskPath);

            var fatManager = new FatTableManager(disk);
            var directoryManager = new DirectoryManager(disk, fatManager);

            if (fatManager.GetFatEntry(FsConstants.ROOT_DIR_FIRST_CLUSTER) == FsConstants.FAT_ENTRY_FREE)
            {
                Console.WriteLine("\u001b[33m[System] Unformatted disk detected. Initializing File System...\u001b[0m");
                InitializeNewDisk(fatManager, disk);
            }

            var shell = new Shell(disk, fatManager, directoryManager);
            shell.Run();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\u001b[31m[FATAL ERROR]:\u001b[0m {ex.Message}");
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }
        finally
        {
            disk.CloseDisk();
        }
    }

    static void InitializeNewDisk(FatTableManager fat, VirtualDisk disk)
    {
        int[] initialFat = new int[FsConstants.FAT_ARRAY_SIZE];

        for (int i = 0; i < FsConstants.FAT_ARRAY_SIZE; i++)
            initialFat[i] = FsConstants.FAT_ENTRY_FREE;

        initialFat[FsConstants.SUPERBLOCK_CLUSTER] = FsConstants.FAT_ENTRY_EOF;
        for (int i = FsConstants.FAT_START_CLUSTER; i <= FsConstants.FAT_END_CLUSTER; i++)
        {
            initialFat[i] = FsConstants.FAT_ENTRY_EOF;
        }

        initialFat[FsConstants.ROOT_DIR_FIRST_CLUSTER] = FsConstants.FAT_ENTRY_EOF;

        fat.WriteAllFat(initialFat);
        fat.FlushFatToDisk();

        byte[] emptyCluster = new byte[FsConstants.CLUSTER_SIZE];
        disk.WriteCluster(FsConstants.ROOT_DIR_FIRST_CLUSTER, emptyCluster);

        Console.WriteLine("\u001b[32m[System] File system structure created successfully.\u001b[0m");
    }
}