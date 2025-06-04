using System;

namespace UvfLib.Common
{
    /// <summary>
    /// Exception thrown for PKCS#12 related errors.
    /// </summary>
    public class Pkcs12Exception : Exception
    {
        /// <summary>
        /// Creates a new PKCS#12 exception.
        /// </summary>
        public Pkcs12Exception() : base() { }

        /// <summary>
        /// Creates a new PKCS#12 exception with the specified message.
        /// </summary>
        /// <param name="message">The exception message</param>
        public Pkcs12Exception(string message) : base(message) { }

        /// <summary>
        /// Creates a new PKCS#12 exception with the specified message and inner exception.
        /// </summary>
        /// <param name="message">The exception message</param>
        /// <param name="innerException">The inner exception</param>
        public Pkcs12Exception(string message, Exception innerException) : base(message, innerException) { }
    }
} 