using System;

namespace UvfLib.Common
{
    /// <summary>
    /// Exception thrown when a PKCS#12 password is incorrect.
    /// </summary>
    public class Pkcs12PasswordException : Pkcs12Exception
    {
        /// <summary>
        /// Creates a new PKCS#12 password exception.
        /// </summary>
        public Pkcs12PasswordException() : base("Invalid PKCS#12 password") { }

        /// <summary>
        /// Creates a new PKCS#12 password exception with the specified message.
        /// </summary>
        /// <param name="message">The exception message</param>
        public Pkcs12PasswordException(string message) : base(message) { }

        /// <summary>
        /// Creates a new PKCS#12 password exception with the specified message and inner exception.
        /// </summary>
        /// <param name="message">The exception message</param>
        /// <param name="innerException">The inner exception</param>
        public Pkcs12PasswordException(string message, Exception innerException) : base(message, innerException) { }
    }
} 