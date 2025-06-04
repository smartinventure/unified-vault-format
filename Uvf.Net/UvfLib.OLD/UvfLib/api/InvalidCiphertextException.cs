using System;

namespace UvfLib.Api
{
    /// <summary>
    /// Exception thrown when ciphertext is invalid.
    /// </summary>
    public class InvalidCiphertextException : CryptoException
    {
        /// <summary>
        /// Creates a new InvalidCiphertextException.
        /// </summary>
        public InvalidCiphertextException() : base() { }

        /// <summary>
        /// Creates a new InvalidCiphertextException with the specified message.
        /// </summary>
        /// <param name="message">The exception message</param>
        public InvalidCiphertextException(string message) : base(message) { }

        /// <summary>
        /// Creates a new InvalidCiphertextException with the specified message and inner exception.
        /// </summary>
        /// <param name="message">The exception message</param>
        /// <param name="innerException">The inner exception</param>
        public InvalidCiphertextException(string message, Exception innerException) : base(message, innerException) { }
    }
}