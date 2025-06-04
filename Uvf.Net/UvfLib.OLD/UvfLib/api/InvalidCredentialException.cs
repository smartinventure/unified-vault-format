using System;

namespace UvfLib.Api
{
    /// <summary>
    /// Thrown when credentials cannot be used to decrypt or authenticate data.
    /// </summary>
    public class InvalidCredentialException : CryptoException
    {
        /// <summary>
        /// Creates a new InvalidCredentialException.
        /// </summary>
        public InvalidCredentialException() : base() { }
        
        /// <summary>
        /// Creates a new InvalidCredentialException with the specified message.
        /// </summary>
        /// <param name="message">The exception message</param>
        public InvalidCredentialException(string message) : base(message) { }
        
        /// <summary>
        /// Creates a new InvalidCredentialException with the specified message and inner exception.
        /// </summary>
        /// <param name="message">The exception message</param>
        /// <param name="innerException">The inner exception</param>
        public InvalidCredentialException(string message, Exception innerException) : base(message, innerException) { }
    }
} 