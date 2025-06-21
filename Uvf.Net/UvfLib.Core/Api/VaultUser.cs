using System;

namespace UvfLib.Core.Api
{
    /// <summary>
    /// Represents a user in a multi-user vault.
    /// </summary>
    public class VaultUser
    {
        /// <summary>
        /// Unique identifier for the user.
        /// </summary>
        public string UserId { get; set; } = string.Empty;

        /// <summary>
        /// Display name for the user (optional).
        /// </summary>
        public string? DisplayName { get; set; }

        /// <summary>
        /// When the user was added to the vault.
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// Last time the user accessed the vault (if tracked).
        /// </summary>
        public DateTime? LastAccessAt { get; set; }

        /// <summary>
        /// User's role in the vault.
        /// </summary>
        public VaultUserRole Role { get; set; }

        /// <summary>
        /// Creates a new vault user.
        /// </summary>
        /// <param name="userId">User ID</param>
        /// <param name="role">User role</param>
        /// <param name="displayName">Optional display name</param>
        public VaultUser(string userId, VaultUserRole role, string? displayName = null)
        {
            UserId = userId ?? throw new ArgumentNullException(nameof(userId));
            Role = role;
            DisplayName = displayName;
            CreatedAt = DateTime.UtcNow;
        }

        /// <summary>
        /// Parameterless constructor for serialization.
        /// </summary>
        public VaultUser()
        {
        }

        public override string ToString()
        {
            return $"{UserId} ({Role})" + (DisplayName != null ? $" - {DisplayName}" : "");
        }
    }

    /// <summary>
    /// Defines the role of a user in a vault.
    /// </summary>
    public enum VaultUserRole
    {
        /// <summary>
        /// Regular user with read/write access to vault contents.
        /// </summary>
        User,

        /// <summary>
        /// Administrator with full vault management capabilities.
        /// </summary>
        Admin
    }
} 