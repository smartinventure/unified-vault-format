using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Jose;
using System.IO;
using System.Linq;

class Program
{
    static string path1 = @"D:\temp\uvf\IdenticalTestVault2\d";
    static string path2 = @"D:\cyptomatortest\martintest2\d";

    static void Main(string[] args)
    {
        Console.WriteLine("=== FILE AND FOLDER COMPARISON ===");
        Console.WriteLine();

        // Get all files and folders from path1
        Console.WriteLine("PATH1 CONTENTS:");
        if (Directory.Exists(path1))
        {
            var path1Items = GetAllFileSystemEntries(path1).OrderBy(x => x).ToList();
            foreach (string item in path1Items)
            {
                Console.WriteLine($"PATH1: {item}");
            }
            Console.WriteLine($"PATH1 TOTAL: {path1Items.Count} items");
        }
        else
        {
            Console.WriteLine($"PATH1: Directory not found: {path1}");
        }

        Console.WriteLine();

        // Get all files and folders from path2
        Console.WriteLine("PATH2 CONTENTS:");
        if (Directory.Exists(path2))
        {
            var path2Items = GetAllFileSystemEntries(path2).OrderBy(x => x).ToList();
            foreach (string item in path2Items)
            {
                Console.WriteLine($"PATH2: {item}");
            }
            Console.WriteLine($"PATH2 TOTAL: {path2Items.Count} items");
        }
        else
        {
            Console.WriteLine($"PATH2: Directory not found: {path2}");
        }

        Console.WriteLine();
        Console.WriteLine("=== COMPARISON COMPLETE ===");
    }

    static List<string> GetAllFileSystemEntries(string rootPath)
    {
        var allEntries = new List<string>();
        
        try
        {
            // Get all files and directories recursively
            string[] allItems = Directory.GetFileSystemEntries(rootPath, "*", SearchOption.AllDirectories);
            allEntries.AddRange(allItems);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error accessing {rootPath}: {ex.Message}");
        }

        return allEntries;
    }
}
