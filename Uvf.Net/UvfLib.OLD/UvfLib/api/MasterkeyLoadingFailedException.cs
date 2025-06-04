using System;

namespace UvfLib.Api
{
    /// <summary>
    /// Thrown when a master key could not be loaded.
    /// </summary>
    public class MasterkeyLoadingFailedException : CryptoException
    {
        public MasterkeyLoadingFailedException() : base() { }
        
        public MasterkeyLoadingFailedException(string message) : base(message) { }
        
        public MasterkeyLoadingFailedException(string message, Exception innerException) : base(message, innerException) { }
    }
} 