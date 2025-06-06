using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.IO;
using System.Linq;

class Program
{
    static string path1 = @"D:\temp\uvf\IdenticalTestVault2";
    static string path2 = @"D:\cyptomatortest\martintest2";
    
    static void Main(string[] args)
    {
        Console.WriteLine("=== Vault File Comparison Tool ===");
        Console.WriteLine($"Path 1: {path1}");
        Console.WriteLine($"Path 2: {path2}");
        Console.WriteLine();
        
        try
        {
            // Get all files from both paths
            var files1 = GetAllFiles(path1);
            var files2 = GetAllFiles(path2);
            
            Console.WriteLine($"Found {files1.Count} files in Path 1");
            Console.WriteLine($"Found {files2.Count} files in Path 2");
            Console.WriteLine();
            
            // Compare files
            var results = CompareFiles(files1, files2);
            
            // Print summary
            PrintSummary(results, files1, files2);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
        
        Console.WriteLine("\nPress any key to exit...");
        Console.ReadKey();
    }
    
    static Dictionary<string, FileInfo> GetAllFiles(string rootPath)
    {
        var files = new Dictionary<string, FileInfo>();
        
        if (!Directory.Exists(rootPath))
        {
            Console.WriteLine($"Warning: Directory not found: {rootPath}");
            return files;
        }
        
        var allFiles = Directory.GetFiles(rootPath, "*", SearchOption.AllDirectories);
        
        foreach (string filePath in allFiles)
        {
            try
            {
                string relativePath = Path.GetRelativePath(rootPath, filePath);
                var fileInfo = new FileInfo(filePath);
                files[relativePath] = fileInfo;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Could not process file {filePath}: {ex.Message}");
            }
        }
        
        return files;
    }
    
    static ComparisonResults CompareFiles(Dictionary<string, FileInfo> files1, Dictionary<string, FileInfo> files2)
    {
        var results = new ComparisonResults();
        
        Console.WriteLine("=== FILE-BY-FILE COMPARISON ===");
        
        // Compare files from path1
        foreach (var kvp1 in files1)
        {
            string relativePath = kvp1.Key;
            var file1 = kvp1.Value;
            
            if (files2.ContainsKey(relativePath))
            {
                var file2 = files2[relativePath];
                
                try
                {
                    string md5_1 = CalculateMD5(file1.FullName);
                    string md5_2 = CalculateMD5(file2.FullName);
                    
                    bool matches = md5_1.Equals(md5_2, StringComparison.OrdinalIgnoreCase);
                    
                    if (matches)
                    {
                        Console.WriteLine($"✅ MATCH: {relativePath}");
                        Console.WriteLine($"   Size: {file1.Length} bytes | MD5: {md5_1}");
                        results.MatchingFiles.Add(relativePath);
                    }
                    else
                    {
                        Console.WriteLine($"❌ DIFFER: {relativePath}");
                        Console.WriteLine($"   Path1: {file1.Length} bytes | MD5: {md5_1}");
                        Console.WriteLine($"   Path2: {file2.Length} bytes | MD5: {md5_2}");
                        results.DifferentFiles.Add(relativePath);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️  ERROR: {relativePath} - {ex.Message}");
                    results.ErrorFiles.Add(relativePath);
                }
            }
            else
            {
                Console.WriteLine($"📁 ONLY IN PATH1: {relativePath} ({file1.Length} bytes)");
                results.OnlyInPath1.Add(relativePath);
            }
            
            Console.WriteLine();
        }
        
        // Find files only in path2
        foreach (var kvp2 in files2)
        {
            string relativePath = kvp2.Key;
            var file2 = kvp2.Value;
            
            if (!files1.ContainsKey(relativePath))
            {
                Console.WriteLine($"📁 ONLY IN PATH2: {relativePath} ({file2.Length} bytes)");
                results.OnlyInPath2.Add(relativePath);
                Console.WriteLine();
            }
        }
        
        return results;
    }
    
    static string CalculateMD5(string filePath)
    {
        using var md5 = MD5.Create();
        using var stream = File.OpenRead(filePath);
        byte[] hashBytes = md5.ComputeHash(stream);
        return Convert.ToHexString(hashBytes);
    }
    
    static void PrintSummary(ComparisonResults results, Dictionary<string, FileInfo> files1, Dictionary<string, FileInfo> files2)
    {
        Console.WriteLine("=== SUMMARY ===");
        Console.WriteLine();
        
        Console.WriteLine($"📊 STATISTICS:");
        Console.WriteLine($"   Total files in Path 1: {files1.Count}");
        Console.WriteLine($"   Total files in Path 2: {files2.Count}");
        Console.WriteLine($"   Files found in both:    {results.MatchingFiles.Count + results.DifferentFiles.Count}");
        Console.WriteLine();
        
        Console.WriteLine($"🎯 COMPARISON RESULTS:");
        Console.WriteLine($"   ✅ Identical files:     {results.MatchingFiles.Count}");
        Console.WriteLine($"   ❌ Different files:     {results.DifferentFiles.Count}");
        Console.WriteLine($"   📁 Only in Path 1:      {results.OnlyInPath1.Count}");
        Console.WriteLine($"   📁 Only in Path 2:      {results.OnlyInPath2.Count}");
        Console.WriteLine($"   ⚠️  Errors:             {results.ErrorFiles.Count}");
        Console.WriteLine();
        
        if (results.OnlyInPath1.Count > 0)
        {
            Console.WriteLine($"📁 FILES ONLY IN PATH 1 ({results.OnlyInPath1.Count}):");
            foreach (string file in results.OnlyInPath1.Take(10))
            {
                Console.WriteLine($"   - {file}");
            }
            if (results.OnlyInPath1.Count > 10)
            {
                Console.WriteLine($"   ... and {results.OnlyInPath1.Count - 10} more");
            }
            Console.WriteLine();
        }
        
        if (results.OnlyInPath2.Count > 0)
        {
            Console.WriteLine($"📁 FILES ONLY IN PATH 2 ({results.OnlyInPath2.Count}):");
            foreach (string file in results.OnlyInPath2.Take(10))
            {
                Console.WriteLine($"   - {file}");
            }
            if (results.OnlyInPath2.Count > 10)
            {
                Console.WriteLine($"   ... and {results.OnlyInPath2.Count - 10} more");
            }
            Console.WriteLine();
        }
        
        // Calculate match percentage
        int totalComparableFiles = results.MatchingFiles.Count + results.DifferentFiles.Count;
        if (totalComparableFiles > 0)
        {
            double matchPercentage = (double)results.MatchingFiles.Count / totalComparableFiles * 100;
            Console.WriteLine($"📈 MATCH RATE: {results.MatchingFiles.Count}/{totalComparableFiles} ({matchPercentage:F1}%)");
        }
        else
        {
            Console.WriteLine($"📈 MATCH RATE: No comparable files found");
        }
    }
}

class ComparisonResults
{
    public List<string> MatchingFiles { get; } = new List<string>();
    public List<string> DifferentFiles { get; } = new List<string>();
    public List<string> OnlyInPath1 { get; } = new List<string>();
    public List<string> OnlyInPath2 { get; } = new List<string>();
    public List<string> ErrorFiles { get; } = new List<string>();
}
