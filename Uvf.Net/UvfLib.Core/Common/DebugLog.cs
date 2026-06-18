using System;

namespace UvfLib.Core.Common
{
    /// <summary>
    /// Lightweight debug-logging gate for the library. Off by default; enable by setting the
    /// environment variable <c>UVFLIB_DEBUG</c> to "1" or "true". Diagnostic Console output across the
    /// library is guarded by <see cref="IsEnabled"/> so the library stays quiet for normal consumers
    /// (e.g. the native FFI demos), which otherwise saw noisy "🔍 ..." lines on stdout.
    /// </summary>
    public static class DebugLog
    {
        // Evaluated once at first use; the env var is not expected to change during a process.
        private static readonly bool _enabled = DetermineEnabled();

        /// <summary>True when <c>UVFLIB_DEBUG</c> is set to "1" or "true" (case-insensitive).</summary>
        public static bool IsEnabled => _enabled;

        private static bool DetermineEnabled()
        {
            string? value = Environment.GetEnvironmentVariable("UVFLIB_DEBUG");
            if (string.IsNullOrEmpty(value)) return false;
            return value.Equals("1", StringComparison.OrdinalIgnoreCase)
                || value.Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Writes a line to the console only when debug logging is enabled.</summary>
        public static void Log(string message)
        {
            if (_enabled)
            {
                Console.WriteLine(message);
            }
        }
    }
}
