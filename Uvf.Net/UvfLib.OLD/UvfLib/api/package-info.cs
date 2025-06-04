namespace UvfLib.Api
{
    /// <summary>
    /// The API namespace contains the interfaces for the Uvf cryptographic library.
    /// 
    /// The central component is the <see cref="Cryptor"/> interface, which provides cryptographic operations
    /// for file names, file headers, and file content. To obtain a <see cref="Cryptor"/> instance, 
    /// a <see cref="CryptorProvider"/> is needed, which can be obtained for a specific cryptographic scheme.
    /// 
    /// The API is designed to be used with a <see cref="Masterkey"/>, which can be loaded using a 
    /// <see cref="MasterkeyLoader"/>. The masterkey is used to derive the keys for the actual encryption operations.
    /// </summary>
} 