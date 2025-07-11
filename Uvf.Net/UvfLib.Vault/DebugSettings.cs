namespace UvfLib.Vault
{
    /// <summary>
    /// Global debug settings for UVF library
    /// </summary>
    public static class DebugSettings
    {
        /// <summary>
        /// Controls whether verbose debug output is shown during encryption/decryption operations
        /// </summary>
        public static bool EnableVerboseDebug { get; set; } = true;
        
        /// <summary>
        /// Set verbose debug mode (helper method)
        /// </summary>
        public static void SetVerboseDebug(bool enabled)
        {
            EnableVerboseDebug = enabled;
            // Also set environment variable for backward compatibility
            Environment.SetEnvironmentVariable("UVF_DEBUG_VERBOSE", enabled ? "true" : "false");
        }
    }
} 