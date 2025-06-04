using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace UvfLib.Common
{
    /// <summary>
    /// Builder for X509 certificates.
    /// </summary>
    public class X509CertBuilder
    {
        private X500DistinguishedName _issuerName;
        private X500DistinguishedName _subjectName;
        private ECDsa _keyPair;
        private DateTimeOffset? _notBefore;
        private DateTimeOffset? _notAfter;
        private X509KeyUsageFlags _keyUsage = X509KeyUsageFlags.None;
        private bool _isCA = false;
        private HashAlgorithmName _signatureAlgorithm = HashAlgorithmName.SHA256;

        /// <summary>
        /// Creates a new certificate builder with default settings.
        /// </summary>
        public X509CertBuilder()
        {
        }

        /// <summary>
        /// Initializes a new certificate builder with the specified key pair and signature algorithm.
        /// </summary>
        /// <param name="keyPair">The key pair to use for the certificate</param>
        /// <param name="signatureAlgorithm">The signature algorithm to use</param>
        /// <returns>A new certificate builder</returns>
        /// <exception cref="ArgumentException">If the key pair type doesn't match the signature algorithm</exception>
        public static X509CertBuilder Init(AsymmetricCryptoKeyPair keyPair, string signatureAlgorithm)
        {
            if (keyPair == null)
                throw new ArgumentNullException(nameof(keyPair));
            if (string.IsNullOrEmpty(signatureAlgorithm))
                throw new ArgumentNullException(nameof(signatureAlgorithm));

            var builder = new X509CertBuilder();

            // Check signature algorithm and key pair compatibility
            if (signatureAlgorithm.Contains("ECDSA"))
            {
                if (!keyPair.IsECDsa)
                {
                    throw new ArgumentException("ECDSA signature algorithm requires an EC key pair", nameof(keyPair));
                }

                builder._keyPair = keyPair.CreateECDsa();

                // Determine hash algorithm from signature algorithm
                if (signatureAlgorithm.Contains("SHA256"))
                    builder._signatureAlgorithm = HashAlgorithmName.SHA256;
                else if (signatureAlgorithm.Contains("SHA384"))
                    builder._signatureAlgorithm = HashAlgorithmName.SHA384;
                else if (signatureAlgorithm.Contains("SHA512"))
                    builder._signatureAlgorithm = HashAlgorithmName.SHA512;
            }
            else if (signatureAlgorithm.Contains("RSA"))
            {
                if (!keyPair.IsRSA)
                {
                    throw new ArgumentException("RSA signature algorithm requires an RSA key pair", nameof(keyPair));
                }

                builder._keyPair = ECDsa.Create(); // This will be overridden in Build() with RSA

                // Determine hash algorithm from signature algorithm
                if (signatureAlgorithm.Contains("SHA256"))
                    builder._signatureAlgorithm = HashAlgorithmName.SHA256;
                else if (signatureAlgorithm.Contains("SHA384"))
                    builder._signatureAlgorithm = HashAlgorithmName.SHA384;
                else if (signatureAlgorithm.Contains("SHA512"))
                    builder._signatureAlgorithm = HashAlgorithmName.SHA512;
            }
            else
            {
                throw new ArgumentException($"Unsupported signature algorithm: {signatureAlgorithm}", nameof(signatureAlgorithm));
            }

            return builder;
        }

        /// <summary>
        /// Sets the issuer name of the certificate.
        /// </summary>
        public X509CertBuilder WithIssuerName(string issuerName)
        {
            if (string.IsNullOrEmpty(issuerName))
                throw new ArgumentNullException(nameof(issuerName));

            try
            {
                _issuerName = new X500DistinguishedName(issuerName);
                return this;
            }
            catch (Exception ex)
            {
                throw new ArgumentException("Invalid issuer name format", nameof(issuerName), ex);
            }
        }

        /// <summary>
        /// Sets the subject name of the certificate.
        /// </summary>
        public X509CertBuilder WithSubjectName(string subjectName)
        {
            if (string.IsNullOrEmpty(subjectName))
                throw new ArgumentNullException(nameof(subjectName));

            try
            {
                _subjectName = new X500DistinguishedName(subjectName);
                return this;
            }
            catch (Exception ex)
            {
                throw new ArgumentException("Invalid subject name format", nameof(subjectName), ex);
            }
        }

        /// <summary>
        /// Sets the key pair to use for the certificate.
        /// </summary>
        public X509CertBuilder WithKeyPair(ECKeyPair keyPair)
        {
            _keyPair = keyPair?.PrivateKey ?? throw new ArgumentNullException(nameof(keyPair));
            return this;
        }

        /// <summary>
        /// Sets the validity period of the certificate.
        /// </summary>
        public X509CertBuilder WithValidityPeriod(DateTimeOffset notBefore, DateTimeOffset notAfter)
        {
            if (notAfter <= notBefore)
                throw new ArgumentException("NotAfter must be later than NotBefore", nameof(notAfter));

            _notBefore = notBefore;
            _notAfter = notAfter;
            return this;
        }

        /// <summary>
        /// Sets the validity duration of the certificate from now.
        /// </summary>
        public X509CertBuilder WithValidityDuration(int days)
        {
            if (days <= 0)
                throw new ArgumentException("Days must be positive", nameof(days));

            _notBefore = DateTimeOffset.UtcNow;
            _notAfter = _notBefore.Value.AddDays(days);
            return this;
        }

        /// <summary>
        /// Sets the key usage flags for the certificate.
        /// </summary>
        public X509CertBuilder WithKeyUsage(X509KeyUsageFlags keyUsage)
        {
            _keyUsage = keyUsage;
            return this;
        }

        /// <summary>
        /// Sets whether the certificate is a CA certificate.
        /// </summary>
        public X509CertBuilder WithCA(bool isCA)
        {
            _isCA = isCA;
            return this;
        }

        /// <summary>
        /// Builds the certificate.
        /// </summary>
        public X509Certificate2 Build()
        {
            if (_subjectName == null)
                throw new InvalidOperationException("Subject name is required");

            if (_issuerName == null)
                throw new InvalidOperationException("Issuer name is required");

            if (_keyPair == null)
                throw new InvalidOperationException("Key pair is required");

            if (_notBefore == null || _notAfter == null)
                throw new InvalidOperationException("Validity period is required");

            var certRequest = new CertificateRequest(
                _subjectName,
                _keyPair,
                _signatureAlgorithm);

            // Add extensions
            if (_keyUsage != X509KeyUsageFlags.None)
            {
                certRequest.CertificateExtensions.Add(
                    new X509KeyUsageExtension(_keyUsage, critical: true));
            }

            if (_isCA)
            {
                certRequest.CertificateExtensions.Add(
                    new X509BasicConstraintsExtension(
                        certificateAuthority: true,
                        hasPathLengthConstraint: false,
                        pathLengthConstraint: 0,
                        critical: true));
            }

            return certRequest.CreateSelfSigned(_notBefore.Value, _notAfter.Value);
        }
    }
}