using System;

namespace UvfLib.Api
{
    /// <summary>
    /// Thrown when a vault format is not supported.
    /// </summary>
    public class UnsupportedVaultFormatException : CryptoException
    {
        /// <summary>
        /// Gets the URI of the masterkey file.
        /// </summary>
        public Uri MasterkeyFileUri { get; }

        /// <summary>
        /// Gets the type of the vault format.
        /// </summary>
        public VaultFormat VaultFormat { get; }

        /// <summary>
        /// Creates a new exception instance.
        /// </summary>
        /// <param name="masterkeyFileUri">The URI of the masterkey file</param>
        /// <param name="detectedVaultFormat">The detected vault format</param>
        public UnsupportedVaultFormatException(Uri masterkeyFileUri, VaultFormat detectedVaultFormat)
            : base($"Unsupported vault format {detectedVaultFormat} detected in {masterkeyFileUri}")
        {
            MasterkeyFileUri = masterkeyFileUri;
            VaultFormat = detectedVaultFormat;
        }

        /// <summary>
        /// Creates a new exception instance.
        /// </summary>
        /// <param name="masterkeyFileUri">The URI of the masterkey file</param>
        /// <param name="detectedVaultFormat">The detected vault format</param>
        /// <param name="message">The error message</param>
        public UnsupportedVaultFormatException(Uri masterkeyFileUri, VaultFormat detectedVaultFormat, string message)
            : base(message)
        {
            MasterkeyFileUri = masterkeyFileUri;
            VaultFormat = detectedVaultFormat;
        }

        /// <summary>
        /// Creates a new exception instance.
        /// </summary>
        /// <param name="masterkeyFileUri">The URI of the masterkey file</param>
        /// <param name="detectedVaultFormat">The detected vault format</param>
        /// <param name="message">The error message</param>
        /// <param name="innerException">The inner exception</param>
        public UnsupportedVaultFormatException(Uri masterkeyFileUri, VaultFormat detectedVaultFormat, string message, Exception innerException)
            : base(message, innerException)
        {
            MasterkeyFileUri = masterkeyFileUri;
            VaultFormat = detectedVaultFormat;
        }
    }

    /// <summary>
    /// Represents the format of a vault.
    /// </summary>
    public enum VaultFormat
    {
        /// <summary>
        /// Cryptomator Vault Format 1 to 8
        /// </summary>
        CryptomatorVaultFormat = 0,

        /// <summary>
        /// Universal Vault Format
        /// </summary>
        UniversalVaultFormat = 1,

        /// <summary>
        /// Unknown vault format
        /// </summary>
        Unknown = 255,
    }
} 