using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace MiniFatFs
{
    public class Shell
    {
        private readonly VirtualDisk _disk;
        private readonly FatTableManager _fat;
        private readonly DirectoryManager _dir;

        private int _currentCluster = FsConstants.ROOT_DIR_FIRST_CLUSTER;
        private string _currentPath = "/";

        public Shell(VirtualDisk disk, FatTableManager fat, DirectoryManager dir)
        {
            _disk = disk; _fat = fat; _dir = dir;
        }

        public void Run()
        {
            Console.Clear();
            PrintLogo();
            
            while (true)
            {
                // ANSI Colors: Green User, Blue Directory
                Console.Write($"\u001b[32muser@nobara\u001b[0m:\u001b[34;1m{_currentPath}\u001b[0m$ ");
                string input = Console.ReadLine()?.Trim();
                if (string.IsNullOrEmpty(input)) continue;

                string[] parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                string cmd = parts[0].ToLower();

                try {
                    switch (cmd) {
                        case "ls": List(); break;
                        case "cd": ChangeDirectory(parts); break;
                        case "mkdir": MakeDirectory(parts); break;
                        case "touch": CreateEmptyFile(parts); break;
                        case "cat": Concatenate(parts); break;
                        case "echo": HandleEcho(input); break;
                        case "rm": HandleRm(parts); break;
                        case "stat": ShowStat(parts); break;
                        case "df": DiskUsage(); break;
                        case "clear": Console.Clear(); break;
                        case "help": ShowHelp(); break;
                        case "exit": return;
                        default: Console.WriteLine($"{cmd}: command not found"); break;
                    }
                } catch (Exception e) { 
                    Console.WriteLine($"\u001b[31msh error:\u001b[0m {e.Message}"); 
                }
            }
        }

        private void List()
        {
            var entries = _dir.ReadDirectory(_currentCluster);
            foreach (var e in entries)
            {
                string attr = (e.Attribute == 0x10) ? "drwx" : "-rwx";
                string color = (e.Attribute == 0x10) ? "\u001b[34;1m" : "\u001b[0m";
                Console.WriteLine($"{attr}  {color}{e.Name.PadRight(12)}\u001b[0m  {e.FileSize} bytes");
            }
        }

        private void ChangeDirectory(string[] parts)
        {
            if (parts.Length < 2 || parts[1] == "/") {
                _currentCluster = FsConstants.ROOT_DIR_FIRST_CLUSTER;
                _currentPath = "/";
                return;
            }

            string target = parts[1];
            var entry = _dir.FindDirectoryEntry(_currentCluster, target);

            if (entry != null && entry.Attribute == 0x10) {
                _currentCluster = entry.FirstCluster;
                // Handle path string logic
                if (target == "..") {
                    var lastIdx = _currentPath.TrimEnd('/').LastIndexOf('/');
                    _currentPath = lastIdx <= 0 ? "/" : _currentPath.Substring(0, lastIdx);
                } else if (target != ".") {
                    _currentPath = _currentPath == "/" ? $"/{target}" : $"{_currentPath}/{target}";
                }
            } else {
                Console.WriteLine($"cd: {target}: No such directory");
            }
        }

        private void MakeDirectory(string[] parts)
        {
            if (parts.Length < 2) throw new Exception("mkdir: missing operand");
            int newCluster = _fat.AllocateChain(1);
            var entry = new DirectoryEntry(parts[1], 0x10, newCluster, 0);
            _dir.AddDirectoryEntry(_currentCluster, entry);

            // Improvement: Add '.' and '..' for true Linux compatibility
            _dir.AddDirectoryEntry(newCluster, new DirectoryEntry(".", 0x10, newCluster, 0));
            _dir.AddDirectoryEntry(newCluster, new DirectoryEntry("..", 0x10, _currentCluster, 0));
        }

        private void HandleEcho(string rawInput)
        {
            // Simple parser for: echo "text" > file
            var match = System.Text.RegularExpressions.Regex.Match(rawInput, "echo \"(.*)\" (>>|>) (.*)");
            if (!match.Success) {
                Console.WriteLine("Usage: echo \"text\" > filename");
                return;
            }

            string text = match.Groups[1].Value;
            string mode = match.Groups[2].Value;
            string filename = match.Groups[3].Value.Trim();

            byte[] data = Encoding.ASCII.GetBytes(text);
            var entry = _dir.FindDirectoryEntry(_currentCluster, filename);

            if (entry == null) {
                int clus = _fat.AllocateChain((data.Length / FsConstants.CLUSTER_SIZE) + 1);
                entry = new DirectoryEntry(filename, 0x20, clus, data.Length);
                _dir.AddDirectoryEntry(_currentCluster, entry);
            }

            // Write logic: In a real system, we'd follow the chain.
            // For this version, we overwrite the first cluster.
            _disk.WriteCluster(entry.FirstCluster, PadToCluster(data));
        }

        private void Concatenate(string[] parts)
        {
            if (parts.Length < 2) return;
            var entry = _dir.FindDirectoryEntry(_currentCluster, parts[1]);
            if (entry == null) throw new Exception("cat: file not found");

            var chain = _fat.FollowChain(entry.FirstCluster);
            foreach (int c in chain) {
                byte[] data = _disk.ReadCluster(c);
                Console.Write(Encoding.ASCII.GetString(data).TrimEnd('\0'));
            }
            Console.WriteLine();
        }

        private void RecursiveDelete(int startCluster)
        {
            var entries = _dir.ReadDirectory(startCluster);
            foreach (var e in entries)
            {
                if (e.Name.Trim() == "." || e.Name.Trim() == "..") continue;

                if (e.Attribute == 0x10)
                {
                    RecursiveDelete(e.FirstCluster);
                }
                
                _dir.RemoveDirectoryEntry(startCluster, e);
            }
        }

        private void HandleRm(string[] parts)
        {
            if (parts.Length < 2) {
                Console.WriteLine("rm: missing operand");
                return;
            }

            bool recursive = parts.Any(p => p == "-r" || p == "-rf");
            bool force = parts.Any(p => p == "-f" || p == "-rf");
            string targetPattern = parts.Last();

            // Safety Guard: Root Protection
            if (targetPattern == "/" || (targetPattern == "*" && _currentCluster == FsConstants.ROOT_DIR_FIRST_CLUSTER && recursive)) {
                Console.WriteLine("\u001b[31mrm: it is dangerous to operate recursively on '/'\u001b[0m");
                return;
            }

            // Get all entries in the current directory
            var allEntries = _dir.ReadDirectory(_currentCluster);
            
            // Convert wildcard pattern (e.g. FILE*) to a Regex (e.g. ^FILE.*$)
            string regexPattern = "^" + Regex.Escape(targetPattern).Replace("\\*", ".*") + "$";
            var matches = allEntries.Where(e => Regex.IsMatch(e.Name.Trim(), regexPattern, RegexOptions.IgnoreCase)).ToList();

            if (matches.Count == 0) {
                if (!force) Console.WriteLine($"rm: cannot remove '{targetPattern}': No such file or directory");
                return;
            }

            foreach (var entry in matches) {
                if (entry.Name.Trim() == "." || entry.Name.Trim() == "..") continue;

                if (entry.Attribute == 0x10) { // Directory
                    if (!recursive) {
                        Console.WriteLine($"rm: cannot remove '{entry.Name.Trim()}': Is a directory");
                        continue;
                    }
                    RecursiveWipe(entry.FirstCluster);
                }

                _dir.RemoveDirectoryEntry(_currentCluster, entry);
                if (!force) Console.WriteLine($"Removed '{entry.Name.Trim()}'");
            }
        }

        private void RecursiveWipe(int clusterNum)
        {
            var children = _dir.ReadDirectory(clusterNum);
            foreach (var child in children)
            {
                // NEVER follow . or .. or you will loop infinitely
                if (child.Name.Trim() == "." || child.Name.Trim() == "..") continue;

                if (child.Attribute == 0x10)
                {
                    RecursiveWipe(child.FirstCluster);
                }

                // Remove child and free its clusters in FAT
                _dir.RemoveDirectoryEntry(clusterNum, child);
            }
        }

        private void DiskUsage()
        {
            int free = 0;
            for (int i = 0; i < FsConstants.FAT_ARRAY_SIZE; i++)
                if (_fat.GetFatEntry(i) == FsConstants.FAT_ENTRY_FREE) free++;

            Console.WriteLine($"Disk: fatty.bin");
            Console.WriteLine($"Total Clusters: {FsConstants.CLUSTER_COUNT}");
            Console.WriteLine($"Free Clusters:  {free}");
            Console.WriteLine($"Used Clusters:  {FsConstants.CLUSTER_COUNT - free}");
        }

        private byte[] PadToCluster(byte[] input) {
            byte[] output = new byte[FsConstants.CLUSTER_SIZE];
            Array.Copy(input, output, Math.Min(input.Length, FsConstants.CLUSTER_SIZE));
            return output;
        }

        private void CreateEmptyFile(string[] parts) {
            if (parts.Length < 2) return;
            int clus = _fat.AllocateChain(1);
            _dir.AddDirectoryEntry(_currentCluster, new DirectoryEntry(parts[1], 0x20, clus, 0));
        }

        private void ShowStat(string[] parts) {
            if (parts.Length < 2) return;
            var entry = _dir.FindDirectoryEntry(_currentCluster, parts[1]);
            if (entry == null) return;
            Console.WriteLine($"  File: {entry.Name}");
            Console.WriteLine($"  Size: {entry.FileSize}\tBlocks: {(_fat.FollowChain(entry.FirstCluster).Count)}");
            Console.WriteLine($"  Attr: {(entry.Attribute == 0x10 ? "Directory" : "Regular File")}");
            Console.WriteLine($"  Inode: {entry.FirstCluster}");
        }

        private void PrintLogo() {
            Console.WriteLine("\u001b[36m" + @"MiniFatFs: OPSYS" + "\u001b[0m\n");
        }

        private void ShowHelp() {
            Console.WriteLine("Commands: ls, cd, mkdir, touch, cat, echo, rm, stat, df, clear, exit");
            Console.WriteLine("Example: echo \"hello world\" > test.txt");
        }
    }
}