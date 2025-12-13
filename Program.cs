using System;
using System.Linq;
using System.Collections.Generic;
using MiniFatFs;

class Program
{
    static void FormatFat(FatTableManager manager)
    {
        int[] freshFat = new int[FsConstants.FAT_ARRAY_SIZE];
        
        for (int i = 0; i < FsConstants.FAT_ARRAY_SIZE; i++)
        {
            freshFat[i] = FsConstants.FAT_ENTRY_FREE;
        }
        
        freshFat[FsConstants.SUPERBLOCK_CLUSTER] = FsConstants.FAT_ENTRY_EOF;
        for (int i = FsConstants.FAT_START_CLUSTER; i <= FsConstants.FAT_END_CLUSTER; i++)
        {
            freshFat[i] = FsConstants.FAT_ENTRY_EOF;
        }

        manager.WriteAllFat(freshFat);
        manager.FlushFatToDisk();
        Console.WriteLine("\nFAT formatted and flushed (Clusters 0-4 marked as EOF, others free).");
    }

    static void Main(string[] args)
    {
        string diskPath = "fatty.bin";
        var disk = new VirtualDisk();
        
        try
        {
            disk.Initialize(diskPath);
            
            var fatManager = new FatTableManager(disk);
            FormatFat(fatManager);

            Console.WriteLine("\n--- Test 1: Allocate 3 Clusters ---");
            int required = 3;
            int startCluster = fatManager.AllocateChain(required);
            
            Console.WriteLine($"Allocated Chain Start: {startCluster}");
            List<int> chain = fatManager.FollowChain(startCluster);
            
            Console.WriteLine($"Chain: {string.Join(" -> ", chain)}");
            
            bool allocationSuccess = chain.Count == required;
            if (allocationSuccess && chain.Count > 0)
            {
                allocationSuccess = allocationSuccess && 
                                   fatManager.GetFatEntry(chain[0]) == (chain.Count > 1 ? chain[1] : FsConstants.FAT_ENTRY_EOF);
            }
            
            Console.WriteLine(allocationSuccess ? "Allocation and FollowChain SUCCESS." : "Allocation and FollowChain FAILED.");

            Console.WriteLine("\n--- Test 2: Free the Chain ---");
            if (chain.Count > 0)
            {
                fatManager.FreeChain(startCluster);
            }
            
            bool freeSuccess = true;
            foreach (var cluster in chain)
            {
                if (fatManager.GetFatEntry(cluster) != FsConstants.FAT_ENTRY_FREE)
                {
                    freeSuccess = false;
                    break;
                }
            }
            
            Console.WriteLine(freeSuccess ? "FreeChain SUCCESS." : "FreeChain FAILED.");

            fatManager.FlushFatToDisk();
            
            Console.WriteLine("\n--- Test 3: Reload and Verify Persistence ---");
            var freshFatManager = new FatTableManager(disk);
            
            bool persistenceSuccess = true;
            foreach (var cluster in chain)
            {
                if (freshFatManager.GetFatEntry(cluster) != FsConstants.FAT_ENTRY_FREE)
                {
                    persistenceSuccess = false;
                    break;
                }
            }
            
            Console.WriteLine(persistenceSuccess 
                ? "Persistence Check SUCCESS (Clusters are still free after reload)." 
                : "Persistence Check FAILED.");
            
            Console.WriteLine("\nTask 3 Implementation complete and tested.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\nAn error occurred: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
        }
        finally
        {
            disk.CloseDisk();
        }
    }
}