namespace UvfLib.Api
{
    /// <summary>
    /// Thrown when authentication of encrypted data fails, i.e. when the authenticity or integrity
    /// of the data cannot be verified. 
    /// </summary>
    public class AuthenticationFailedException : CryptoException
    {
        public AuthenticationFailedException() : base() { }

        public AuthenticationFailedException(string message) : base(message) { }

        public AuthenticationFailedException(string message, Exception innerException) : base(message, innerException) { }
    }
}