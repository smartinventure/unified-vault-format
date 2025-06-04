using System;

namespace UvfLib.Api
{
    /// <summary>
    /// Base exception for all cryptography-related exceptions.
    /// </summary>
    public class CryptoException : Exception
    {
        public CryptoException() : base() { }
        
        public CryptoException(string message) : base(message) { }
        
        public CryptoException(string message, Exception innerException) : base(message, innerException) { }
    }
} 