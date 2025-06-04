namespace UvfLib.Api
{
    /// <summary>
    /// A masterkey that can be destroyed and provides access to the raw key material.
    /// </summary>
    public interface DestroyableMasterkey : Masterkey
    {
        /// <summary>
        /// Gets a copy of the raw key material. The caller is responsible for securely erasing this data when done.
        /// </summary>
        /// <returns>The raw key material</returns>
        byte[] GetRaw();
    }
}