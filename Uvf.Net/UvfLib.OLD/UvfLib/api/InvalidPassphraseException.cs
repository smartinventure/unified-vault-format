using System;

namespace UvfLib.Api
{
    /// <summary>
    /// Thrown when a passphrase cannot be used to decrypt a key.
    /// </summary>
    public class InvalidPassphraseException : CryptoException
    {
        public InvalidPassphraseException() : base() { }
        
        public InvalidPassphraseException(string message) : base(message) { }
        
        public InvalidPassphraseException(string message, Exception innerException) : base(message, innerException) { }
    }
} 