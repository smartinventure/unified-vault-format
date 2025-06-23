namespace DemoApp.Wrapper
{
    /// <summary>
    /// A write-only memory stream that writes back to the vault when disposed
    /// </summary>
    internal class TitanVaultWriteStream : MemoryStream
    {
        private readonly TitanVault _vault;
        private readonly string _filePath;
        private bool _disposed;

        public TitanVaultWriteStream(TitanVault vault, string filePath)
        {
            _vault = vault;
            _filePath = filePath;
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                // Write the data back to the vault
                var data = ToArray();
                _vault.WriteAllBytes(_filePath, data);
                _disposed = true;
            }
            base.Dispose(disposing);
        }
    }
} 