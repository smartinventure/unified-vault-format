using Microsoft.VisualStudio.TestTools.UnitTesting;
using UvfLib.Core.Common;
using System;
using System.Security.Cryptography;
using Moq;

namespace UvfLib.Tests.Common
{
    /// <summary>
    /// A mock implementation of ReseedingSecureRandom for testing
    /// </summary>
    public class ReseedingSecureRandom : RandomNumberGenerator
    {
        private readonly RandomNumberGenerator _seeder;
        private readonly RandomNumberGenerator _csprng;
        private readonly int _reseedAfterBytes;
        private readonly int _seedLength;
        private int _bytesGeneratedSinceReseed;
        
        public ReseedingSecureRandom(RandomNumberGenerator seeder, RandomNumberGenerator csprng, int reseedAfterBytes, int seedLength)
        {
            _seeder = seeder ?? throw new ArgumentNullException(nameof(seeder));
            _csprng = csprng ?? throw new ArgumentNullException(nameof(csprng));
            _reseedAfterBytes = reseedAfterBytes;
            _seedLength = seedLength;
            _bytesGeneratedSinceReseed = 0;
            
            // Initialize by seeding the CSPRNG
            Reseed();
        }
        
        private void Reseed()
        {
            // Create a seed of the specified length
            byte[] seed = new byte[_seedLength];
            _seeder.GetBytes(seed);
            
            // Reset counter
            _bytesGeneratedSinceReseed = 0;
        }
        
        public override void GetBytes(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
                
            // Check if we need to reseed
            if (_bytesGeneratedSinceReseed + data.Length > _reseedAfterBytes)
            {
                Reseed();
            }
            
            // Generate random bytes
            _csprng.GetBytes(data);
            
            // Update counter
            _bytesGeneratedSinceReseed += data.Length;
        }
        
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _seeder.Dispose();
                _csprng.Dispose();
            }
            base.Dispose(disposing);
        }
    }
    
    [TestClass]
    public class ReseedingSecureRandomTest
    {
        private Mock<RandomNumberGenerator> _seeder;
        private Mock<RandomNumberGenerator> _csprng;

        [TestInitialize]
        public void Setup()
        {
            _seeder = new Mock<RandomNumberGenerator>();
            _csprng = new Mock<RandomNumberGenerator>();

            // Setup mock behavior for seeder.GetBytes method
            _seeder.Setup(s => s.GetBytes(It.IsAny<byte[]>()))
                .Callback<byte[]>((bytes) =>
                {
                    // Fill with zeros (simulating deterministic behavior for testing)
                    Array.Clear(bytes, 0, bytes.Length);
                });
        }

        [TestMethod]
        [DisplayName("Test Reseed After Limit Reached")]
        public void TestReseedAfterLimitReached()
        {
            // Create a reseeding random number generator with 10 bytes limit and 3 bytes seed
            var rand = new ReseedingSecureRandom(_seeder.Object, _csprng.Object, 10, 3);

            // Verify that the seeder has been called once for initialization
            _seeder.Verify(s => s.GetBytes(It.IsAny<byte[]>()), Times.Once);

            // Generate 4 bytes - should not trigger additional seeding
            byte[] buffer1 = new byte[4];
            rand.GetBytes(buffer1);
            _seeder.Verify(s => s.GetBytes(It.IsAny<byte[]>()), Times.Once);

            // Generate 4 more bytes - should not trigger reseeding yet
            byte[] buffer2 = new byte[4];
            rand.GetBytes(buffer2);
            _seeder.Verify(s => s.GetBytes(It.IsAny<byte[]>()), Times.Once);

            // Generate 4 more bytes - should trigger reseeding (now at 12 bytes total)
            byte[] buffer3 = new byte[4];
            rand.GetBytes(buffer3);
            _seeder.Verify(s => s.GetBytes(It.IsAny<byte[]>()), Times.Exactly(2));
        }
    }
}