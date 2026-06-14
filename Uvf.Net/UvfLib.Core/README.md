# UvfLib.Core - Cryptographic Core Library

## Overview

UvfLib.Core is the open-source cryptographic foundation for Universal Vault Format (UVF) and Cryptomator vault operations. This library provides low-level APIs for secure file encryption, decryption, and vault management.

## Features

- **UVF Format Support**: Complete implementation of Universal Vault Format v3
- **Cryptomator Compatibility**: Full support for Cryptomator v8 vault format
- **Strong Cryptography**: AES-GCM, AES-SIV, HKDF, and other industry-standard algorithms
- **Cross-Platform**: Built on .NET Standard 2.0 for maximum compatibility
- **Open Source**: AGPL-3.0 licensed for transparency and security auditing (commercial licenses available on request)

## Architecture

### Core Components

- **`Api/`** - Public interfaces and contracts
- **`Common/`** - Cryptographic utilities and helpers
- **`V3/`** - UVF format implementation
- **`CryptomatorV8/`** - Cryptomator compatibility layer

### Key Classes

- `Cryptor` - Main cryptographic operations interface
- `FileContentCryptor` - File encryption/decryption
- `DirectoryContentCryptor` - Directory structure encryption
- `FileHeaderCryptor` - File header management
- `Masterkey` - Key derivation and management

## Usage

This is a low-level library intended for integration into higher-level vault management systems. For easier-to-use APIs, see the companion `UvfLib.FileSystem` package.

```csharp
// Example: Creating a cryptor for UVF format
var factory = new CryptoFactoryImpl();
var masterkey = UVFMasterkeyImpl.Create(password, vaultPath);
var cryptor = factory.CreateCryptor(masterkey);

// Use cryptor for file operations...
```

## Security

This library implements cryptographic operations following industry best practices:

- Authenticated encryption (AES-GCM)
- Deterministic authenticated encryption (AES-SIV) for filenames
- Secure key derivation (HKDF, SCrypt)
- Constant-time operations where applicable
- Secure memory handling

## License

GNU Affero General Public License v3.0 (AGPL-3.0) — see the LICENSE file at the
repository root for details. Alternative commercial licenses are available upon
request (contact info@smartinventure.com).

## Contributing

This project welcomes contributions. Please ensure all cryptographic changes are thoroughly reviewed and tested. 