using Microsoft.VisualStudio.TestTools.UnitTesting;
using UvfLib.Tests.Common.TestUtilities;
using Moq;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UvfLib.Core.Common;

namespace UvfLib.Tests.Common
{
    [TestClass]
    public class MasterkeyFileAccessTest
    {
        // Constants for scrypt parameters matching Java test setup (N=4, r=8, p=1)
        private const int SCRYPT_N_TEST = 4; // Java test used costParam=2, which meant N=2. Correcting to N=4 (2^2) for valid param.
        private const int SCRYPT_R_TEST = 8;
        private const int SCRYPT_P_TEST = 1;
        private const int VAULT_VERSION_TEST = 3; // Matches Java test

        // Use a real RNG for functional tests
        // private static readonly RandomNumberGenerator RANDOM_MOCK = SecureRandomMock.NULL_RANDOM;
        private static readonly RandomNumberGenerator REAL_RANDOM = RandomNumberGenerator.Create();
        private static readonly byte[] DEFAULT_PEPPER = new byte[0];

        private PerpetualMasterkey _key;
        private MasterkeyFileAccess _masterkeyFileAccess;

        [TestInitialize]
        public void Setup()
        {
            _key = new PerpetualMasterkey(new byte[64]);
            // Use REAL_RANDOM
            _masterkeyFileAccess = new MasterkeyFileAccess(DEFAULT_PEPPER, REAL_RANDOM);
        }

        [TestMethod]
        [DisplayName("Test Change Passphrase With MasterkeyFile")]
        public void TestChangePassphraseWithMasterkeyFile()
        {
            // Arrange: Lock a key first to get a valid MasterkeyFile
            var lockedKeyFile = _masterkeyFileAccess.Lock(_key, "asd", VAULT_VERSION_TEST, SCRYPT_N_TEST);

            // Act
            MasterkeyFile changed1 = _masterkeyFileAccess.ChangePassphrase(lockedKeyFile, "asd", "qwe");
            MasterkeyFile changed2 = _masterkeyFileAccess.ChangePassphrase(changed1, "qwe", "asd");

            // Assert: Check that keys are different after first change, and equal after second change
            CollectionAssert.AreNotEqual(lockedKeyFile.EncMasterKey, changed1.EncMasterKey);
            CollectionAssert.AreNotEqual(lockedKeyFile.MacMasterKey, changed1.MacMasterKey);
            // Salt should change
            CollectionAssert.AreNotEqual(lockedKeyFile.ScryptSalt, changed1.ScryptSalt);

            // After changing back, the wrapped keys should be different (due to different salt)
            // but unlocking both lockedKeyFile and changed2 with "asd" should yield the original key.
            CollectionAssert.AreNotEqual(lockedKeyFile.EncMasterKey, changed2.EncMasterKey);
            CollectionAssert.AreNotEqual(lockedKeyFile.MacMasterKey, changed2.MacMasterKey);
            CollectionAssert.AreNotEqual(lockedKeyFile.ScryptSalt, changed2.ScryptSalt);

            // Verify unlock works for both
            using var originalKey = _masterkeyFileAccess.Unlock(lockedKeyFile, "asd");
            using var changedBackKey = _masterkeyFileAccess.Unlock(changed2, "asd");
            CollectionAssert.AreEqual(originalKey.GetRaw(), changedBackKey.GetRaw());
        }

        [TestMethod]
        [DisplayName("Test Read Alleged Vault Version")]
        public void TestReadAllegedVaultVersion()
        {
            byte[] content = Encoding.UTF8.GetBytes("{\"vaultVersion\": 1337}");
            int version = MasterkeyFileAccess.ReadAllegedVaultVersion(content);
            Assert.AreEqual(1337, version);
        }

        [TestClass]
        public class WithSerializedKeyFile
        {
            private PerpetualMasterkey _key;
            private MasterkeyFileAccess _masterkeyFileAccess;
            private byte[] _serializedKeyFile;

            [TestInitialize]
            public void Setup()
            {
                _key = new PerpetualMasterkey(new byte[64]);
                // Use REAL_RANDOM here too
                _masterkeyFileAccess = new MasterkeyFileAccess(DEFAULT_PEPPER, REAL_RANDOM);

                using (MemoryStream out1 = new MemoryStream())
                {
                    // Persist now uses the functional Lock method
                    _masterkeyFileAccess.Persist(_key, out1, "asd", MasterkeyFileAccessTest.VAULT_VERSION_TEST, MasterkeyFileAccessTest.SCRYPT_N_TEST);
                    _serializedKeyFile = out1.ToArray();
                }
            }

            [TestMethod]
            [DisplayName("Test Change Passphrase With Raw Bytes")]
            public void TestChangePassphraseWithRawBytes()
            {
                byte[] changed = _masterkeyFileAccess.ChangePassphrase(_serializedKeyFile, "asd", "qwe");
                byte[] restored = _masterkeyFileAccess.ChangePassphrase(changed, "qwe", "asd");

                CollectionAssert.AreNotEqual(changed, _serializedKeyFile);

                // Verify unlocking works for both
                using var originalKey = _masterkeyFileAccess.Load(new MemoryStream(_serializedKeyFile), "asd");
                using var restoredKey = _masterkeyFileAccess.Load(new MemoryStream(restored), "asd");
                CollectionAssert.AreEqual(originalKey.GetRaw(), restoredKey.GetRaw());
            }

            [TestMethod]
            [DisplayName("Test Load")]
            public void TestLoad()
            {
                using (MemoryStream in1 = new MemoryStream(_serializedKeyFile))
                {
                    PerpetualMasterkey loaded = _masterkeyFileAccess.Load(in1, "asd");
                    CollectionAssert.AreEqual(_key.GetRaw(), loaded.GetRaw());
                }
            }

            [TestMethod]
            [DisplayName("Test Load Invalid Json")]
            public void TestLoadInvalid()
            {
                string content = "{\"foo\": 42}";
                using (MemoryStream in1 = new MemoryStream(Encoding.UTF8.GetBytes(content)))
                {
                    Assert.ThrowsException<IOException>(() =>
                    {
                        _masterkeyFileAccess.Load(in1, "asd");
                    });
                }
            }

            [TestMethod]
            [DisplayName("Test Load Malformed Content")]
            public void TestLoadMalformed()
            {
                string content = "not even json";
                using (MemoryStream in1 = new MemoryStream(Encoding.UTF8.GetBytes(content)))
                {
                    Assert.ThrowsException<IOException>(() =>
                    {
                        _masterkeyFileAccess.Load(in1, "asd");
                    });
                }
            }
        }

        [TestClass]
        public class UnlockTests
        {
            private MasterkeyFile _keyFile;
            private MasterkeyFileAccess _masterkeyFileAccess;
            private PerpetualMasterkey _key;

            [TestInitialize]
            public void Setup()
            {
                _key = new PerpetualMasterkey(new byte[64]);
                _masterkeyFileAccess = new MasterkeyFileAccess(DEFAULT_PEPPER, REAL_RANDOM);
                _keyFile = _masterkeyFileAccess.Lock(_key, "asd", VAULT_VERSION_TEST, SCRYPT_N_TEST);
            }

            [TestMethod]
            [DisplayName("Test Unlock With Correct Password")]
            public void TestUnlockWithCorrectPassword()
            {
                using var unlockedKey = _masterkeyFileAccess.Unlock(_keyFile, "asd");
                Assert.IsNotNull(unlockedKey);
                CollectionAssert.AreEqual(_key.GetRaw(), unlockedKey.GetRaw());
            }

            [TestMethod]
            [DisplayName("Test Unlock With Incorrect Password")]
            public void TestUnlockWithIncorrectPassword()
            {
                Assert.ThrowsException<Core.Api.InvalidCredentialException>(() =>
                {
                    _masterkeyFileAccess.Unlock(_keyFile, "qwe");
                });
            }

            [TestMethod]
            [DisplayName("Test Unlock With Incorrect Pepper")]
            public void TestUnlockWithIncorrectPepper()
            {
                MasterkeyFileAccess masterkeyFileAccessWithPepper = new MasterkeyFileAccess(new byte[1], REAL_RANDOM);

                Assert.ThrowsException<Core.Api.InvalidCredentialException>(() =>
                {
                    masterkeyFileAccessWithPepper.Unlock(_keyFile, "asd");
                });
            }
        }

        [TestClass]
        public class LockTests
        {
            private PerpetualMasterkey _key;
            private MasterkeyFileAccess _masterkeyFileAccess;

            [TestInitialize]
            public void Setup()
            {
                _key = new PerpetualMasterkey(new byte[64]);
                _masterkeyFileAccess = new MasterkeyFileAccess(DEFAULT_PEPPER, REAL_RANDOM);
            }

            [TestMethod]
            [DisplayName("Test Lock Creates Expected Structure")]
            public void TestLock()
            {
                MasterkeyFile keyFile = _masterkeyFileAccess.Lock(_key, "asd", VAULT_VERSION_TEST, SCRYPT_N_TEST);

                Assert.AreEqual(VAULT_VERSION_TEST, keyFile.Version);
                Assert.AreEqual(SCRYPT_N_TEST, keyFile.ScryptCostParam);
                Assert.AreEqual(SCRYPT_R_TEST, keyFile.ScryptBlockSize);

                Assert.IsNotNull(keyFile.ScryptSalt);
                Assert.AreEqual(16, keyFile.ScryptSalt.Length);

                Assert.IsNotNull(keyFile.EncMasterKey);
                Assert.IsTrue(keyFile.EncMasterKey.Length > 0);
                Assert.IsNotNull(keyFile.MacMasterKey);
                Assert.IsTrue(keyFile.MacMasterKey.Length > 0);
                Assert.IsNotNull(keyFile.VersionMac);
                Assert.IsTrue(keyFile.VersionMac.Length > 0);
            }

            [TestMethod]
            [DisplayName("Test Lock With Different Passwords")]
            public void TestLockWithDifferentPasswords()
            {
                MasterkeyFile keyFile1 = _masterkeyFileAccess.Lock(_key, "asd", VAULT_VERSION_TEST, SCRYPT_N_TEST);
                MasterkeyFile keyFile2 = _masterkeyFileAccess.Lock(_key, "qwe", VAULT_VERSION_TEST, SCRYPT_N_TEST);

                CollectionAssert.AreNotEqual(keyFile1.EncMasterKey, keyFile2.EncMasterKey);
                CollectionAssert.AreNotEqual(keyFile1.ScryptSalt, keyFile2.ScryptSalt);
            }

            [TestMethod]
            [DisplayName("Test Lock With Different Peppers")]
            public void TestLockWithDifferentPeppers()
            {
                byte[] pepper1 = new byte[] { 0x01 };
                byte[] pepper2 = new byte[] { 0x02 };
                MasterkeyFileAccess masterkeyFileAccess1 = new MasterkeyFileAccess(pepper1, REAL_RANDOM);
                MasterkeyFileAccess masterkeyFileAccess2 = new MasterkeyFileAccess(pepper2, REAL_RANDOM);

                MasterkeyFile keyFile1 = masterkeyFileAccess1.Lock(_key, "asd", VAULT_VERSION_TEST, SCRYPT_N_TEST);
                MasterkeyFile keyFile2 = masterkeyFileAccess2.Lock(_key, "asd", VAULT_VERSION_TEST, SCRYPT_N_TEST);

                CollectionAssert.AreNotEqual(keyFile1.EncMasterKey, keyFile2.EncMasterKey);
                CollectionAssert.AreNotEqual(keyFile1.ScryptSalt, keyFile2.ScryptSalt);
            }
        }

        [TestMethod]
        [DisplayName("Test Persist And Load")]
        public void TestPersistAndLoad()
        {
            string tempFilePath = Path.GetTempFileName();
            try
            {
                _masterkeyFileAccess.Persist(_key, tempFilePath, "asd", VAULT_VERSION_TEST, SCRYPT_N_TEST);

                PerpetualMasterkey loaded = _masterkeyFileAccess.Load(tempFilePath, "asd");

                CollectionAssert.AreEqual(_key.GetRaw(), loaded.GetRaw());
            }
            finally
            {
                if (File.Exists(tempFilePath))
                {
                    File.Delete(tempFilePath);
                }
            }
        }
    }
}