using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace UvfLib.Common
{
    /// <summary>
    /// Helper class for working with PKCS#12 files.
    /// </summary>
    internal static class Pkcs12Helper
    {
        private const string X509_ISSUER = "CN=Cryptomator";
        private const string X509_SUBJECT = "CN=Self Signed Cert";
        private const int X509_VALID_DAYS = 3560;
        private const string KEYSTORE_ALIAS_KEY = "key";
        private const string KEYSTORE_ALIAS_CERT = "crt";

        /// <summary>
        /// Stores the given key pair in PKCS#12 format.
        /// </summary>
        /// <param name="keyPair">The key pair to export</param>
        /// <param name="output">The output stream to which the result will be written</param>
        /// <param name="passphrase">The password to protect the key material</param>
        /// <param name="signatureAlg">A suited signature algorithm to sign a x509v3 cert holding the public key</param>
        /// <exception cref="IOException">In case of I/O errors</exception>
        /// <exception cref="Pkcs12Exception">If any cryptographic operation fails</exception>
        public static void ExportTo(ECDsa keyPair, Stream output, char[] passphrase, string signatureAlg)
        {
            try
            {
                var certParams = new CertificateRequest(
                    new X500DistinguishedName(X509_SUBJECT),
                    keyPair,
                    GetHashAlgorithmName(signatureAlg));

                // Add Basic Constraints Extension (Not CA, non-critical)
                certParams.CertificateExtensions.Add(
                    new X509BasicConstraintsExtension(false, false, 0, false));

                // Add a default Key Usage extension (e.g., for signing/encryption)
                certParams.CertificateExtensions.Add(
                    new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));

                // Create the self-signed certificate
                using var cert = certParams.CreateSelfSigned(
                    DateTimeOffset.Now.AddDays(-1),
                    DateTimeOffset.Now.AddDays(X509_VALID_DAYS));

                // Assign a friendly name
                cert.FriendlyName = KEYSTORE_ALIAS_CERT;

                // Create a collection and add the certificate
                var collection = new X509Certificate2Collection();
                collection.Add(cert);

                // Export the collection to PKCS#12 format
                var pfxData = collection.Export(X509ContentType.Pfx, new string(passphrase));

                // Write to the output stream
                output.Write(pfxData, 0, pfxData.Length);
                output.Flush();
            }
            catch (Exception ex)
            {
                throw new Pkcs12Exception("Failed to store PKCS12 file.", ex);
            }
        }

        /// <summary>
        /// Loads a key pair from PKCS#12 format.
        /// </summary>
        /// <param name="input">Where to load the key pair from</param>
        /// <param name="passphrase">The password to protect the key material</param>
        /// <returns>The loaded key pair</returns>
        /// <exception cref="IOException">In case of I/O errors</exception>
        /// <exception cref="Pkcs12PasswordException">If the supplied password is incorrect</exception>
        /// <exception cref="Pkcs12Exception">If any cryptographic operation fails</exception>
        public static ECDsa ImportFrom(Stream input, char[] passphrase)
        {
            try
            {
                // Read the entire input stream
                using var ms = new MemoryStream();
                input.CopyTo(ms);
                var data = ms.ToArray();

                // Load the PKCS#12 file
                var cert = new X509Certificate2(
                    data,
                    new string(passphrase),
                    X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);

                // Extract the private key
                var privateKey = cert.GetECDsaPrivateKey();
                if (privateKey == null)
                {
                    throw new Pkcs12Exception("Failed to extract ECDsa private key from certificate");
                }

                return privateKey;
            }
            catch (CryptographicException ex)
            {
                throw new Pkcs12PasswordException("Failed to load PKCS12 file, likely due to incorrect password", ex);
            }
            catch (IOException ex)
            {
                throw ex;
            }
            catch (Exception ex)
            {
                throw new Pkcs12Exception("Failed to load PKCS12 file.", ex);
            }
        }

        private static HashAlgorithmName GetHashAlgorithmName(string signatureAlg)
        {
            if (signatureAlg.Contains("SHA256", StringComparison.OrdinalIgnoreCase))
                return HashAlgorithmName.SHA256;
            if (signatureAlg.Contains("SHA384", StringComparison.OrdinalIgnoreCase))
                return HashAlgorithmName.SHA384;
            if (signatureAlg.Contains("SHA512", StringComparison.OrdinalIgnoreCase))
                return HashAlgorithmName.SHA512;

            throw new ArgumentException($"Unsupported signature algorithm: {signatureAlg}");
        }
    }
}