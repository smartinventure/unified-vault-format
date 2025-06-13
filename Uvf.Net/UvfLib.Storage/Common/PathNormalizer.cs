using System;
using System.IO;

namespace UvfLib.Storage.Common
{
    /// <summary>
    /// Centralized path normalization utility for vault storage operations.
    /// 
    /// Key principles:
    /// - Virtual paths (user-facing) always use forward slashes ("/")
    /// - Physical paths (file system) use platform-specific separators
    /// - Mount point is the vault base directory where "/" maps to
    /// - All path operations are consistent across Windows and Unix systems
    /// </summary>
    public static class PathNormalizer
    {
        /// <summary>
        /// The virtual root path constant
        /// </summary>
        public const string VirtualRoot = "/";

        /// <summary>
        /// Normalizes a virtual path to use forward slashes and proper formatting.
        /// This is what users of the storage API should work with.
        /// 
        /// Examples:
        /// - "" -> "/"
        /// - "folder\file.txt" -> "/folder/file.txt"
        /// - "/folder/file.txt/" -> "/folder/file.txt"
        /// - "\\folder\\file.txt" -> "/folder/file.txt"
        /// </summary>
        /// <param name="virtualPath">The virtual path to normalize</param>
        /// <returns>Normalized virtual path with forward slashes</returns>
        public static string NormalizeVirtualPath(string virtualPath)
        {
            if (string.IsNullOrEmpty(virtualPath))
                return VirtualRoot;

            // Convert all backslashes to forward slashes
            virtualPath = virtualPath.Replace('\\', '/');

            // Ensure it starts with /
            if (!virtualPath.StartsWith('/'))
                virtualPath = "/" + virtualPath;

            // Remove trailing slash (except for root)
            if (virtualPath.Length > 1 && virtualPath.EndsWith('/'))
                virtualPath = virtualPath.TrimEnd('/');

            return virtualPath;
        }

        /// <summary>
        /// Converts a virtual path to a physical path relative to the mount point.
        /// This handles the conversion from forward slashes to platform-specific separators.
        /// 
        /// Examples (Windows):
        /// - "/" -> ""
        /// - "/folder/file.txt" -> "folder\file.txt"
        /// 
        /// Examples (Unix):
        /// - "/" -> ""
        /// - "/folder/file.txt" -> "folder/file.txt"
        /// </summary>
        /// <param name="virtualPath">Normalized virtual path</param>
        /// <returns>Physical path with platform-specific separators</returns>
        public static string VirtualToPhysicalPath(string virtualPath)
        {
            virtualPath = NormalizeVirtualPath(virtualPath);
            
            if (virtualPath == VirtualRoot)
                return string.Empty;

            // Remove leading slash and convert to platform separators
            string physicalPath = virtualPath.Substring(1);
            return physicalPath.Replace('/', Path.DirectorySeparatorChar);
        }

        /// <summary>
        /// Converts a physical path back to a virtual path.
        /// This is the reverse of VirtualToPhysicalPath.
        /// 
        /// Examples (Windows):
        /// - "" -> "/"
        /// - "folder\file.txt" -> "/folder/file.txt"
        /// 
        /// Examples (Unix):
        /// - "" -> "/"
        /// - "folder/file.txt" -> "/folder/file.txt"
        /// </summary>
        /// <param name="physicalPath">Physical path with platform-specific separators</param>
        /// <returns>Normalized virtual path</returns>
        public static string PhysicalToVirtualPath(string physicalPath)
        {
            if (string.IsNullOrEmpty(physicalPath))
                return VirtualRoot;

            // Convert platform separators to forward slashes
            string virtualPath = physicalPath.Replace(Path.DirectorySeparatorChar, '/');
            
            // Ensure it starts with /
            if (!virtualPath.StartsWith('/'))
                virtualPath = "/" + virtualPath;

            return virtualPath;
        }

        /// <summary>
        /// Combines a mount point (vault base path) with a physical path to create
        /// a full file system path. Ensures proper directory separator handling.
        /// 
        /// Examples:
        /// - ("D:\vault", "folder\file.txt") -> "D:\vault\folder\file.txt"
        /// - ("/mnt/vault", "folder/file.txt") -> "/mnt/vault/folder/file.txt"
        /// </summary>
        /// <param name="mountPoint">The vault base directory (mount point)</param>
        /// <param name="physicalPath">The physical path relative to mount point</param>
        /// <returns>Full file system path</returns>
        public static string CombineWithMountPoint(string mountPoint, string physicalPath)
        {
            if (string.IsNullOrEmpty(mountPoint))
                throw new ArgumentException("Mount point cannot be null or empty", nameof(mountPoint));

            if (string.IsNullOrEmpty(physicalPath))
                return mountPoint;

            return Path.Combine(mountPoint, physicalPath);
        }

        /// <summary>
        /// Normalizes a vault directory path returned by VaultHandler.GetDirectoryPath().
        /// These paths use forward slashes but need to be converted to platform separators
        /// for file system operations.
        /// 
        /// Examples:
        /// - "d/XX/YYYYYYYY" -> "d\XX\YYYYYYYY" (Windows)
        /// - "d/XX/YYYYYYYY" -> "d/XX/YYYYYYYY" (Unix)
        /// </summary>
        /// <param name="vaultDirectoryPath">Directory path from VaultHandler</param>
        /// <returns>Path with platform-specific separators</returns>
        public static string NormalizeVaultDirectoryPath(string vaultDirectoryPath)
        {
            if (string.IsNullOrEmpty(vaultDirectoryPath))
                return string.Empty;

            return vaultDirectoryPath.Replace('/', Path.DirectorySeparatorChar);
        }

        /// <summary>
        /// Splits a virtual path into its directory and filename components.
        /// 
        /// Examples:
        /// - "/folder/file.txt" -> ("/folder", "file.txt")
        /// - "/file.txt" -> ("/", "file.txt")
        /// - "/" -> ("/", "")
        /// </summary>
        /// <param name="virtualPath">Normalized virtual path</param>
        /// <returns>Tuple of (directory path, filename)</returns>
        public static (string directoryPath, string filename) SplitVirtualPath(string virtualPath)
        {
            virtualPath = NormalizeVirtualPath(virtualPath);
            
            if (virtualPath == VirtualRoot)
                return (VirtualRoot, string.Empty);

            int lastSlashIndex = virtualPath.LastIndexOf('/');
            if (lastSlashIndex <= 0) // Should not happen with normalized paths, but safety check
                return (VirtualRoot, virtualPath.Substring(1));

            string directoryPath = virtualPath.Substring(0, lastSlashIndex);
            if (string.IsNullOrEmpty(directoryPath))
                directoryPath = VirtualRoot;

            string filename = virtualPath.Substring(lastSlashIndex + 1);
            
            return (directoryPath, filename);
        }

        /// <summary>
        /// Joins virtual path components using forward slashes.
        /// 
        /// Examples:
        /// - ("/folder", "file.txt") -> "/folder/file.txt"
        /// - ("/", "file.txt") -> "/file.txt"
        /// </summary>
        /// <param name="basePath">Base virtual path</param>
        /// <param name="relativePath">Relative path to append</param>
        /// <returns>Combined virtual path</returns>
        public static string JoinVirtualPath(string basePath, string relativePath)
        {
            basePath = NormalizeVirtualPath(basePath);
            
            if (string.IsNullOrEmpty(relativePath))
                return basePath;

            // Remove leading slashes from relative path
            relativePath = relativePath.TrimStart('/', '\\');
            
            if (basePath == VirtualRoot)
                return VirtualRoot + relativePath;
            
            return basePath + "/" + relativePath;
        }
    }
} 