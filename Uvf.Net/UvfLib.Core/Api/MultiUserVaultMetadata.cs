using System;
using System.Collections.Generic;
using System.Linq;

namespace UvfLib.Core.Api
{
    /// <summary>
    /// Contains metadata about a multi-user vault, including user information and key rotation history.
    /// </summary>
    public class MultiUserVaultMetadata
    {
        /// <summary>
        /// List of users who have access to this vault.
        /// </summary>
        public List<VaultUser> Users { get; set; } = new();

        /// <summary>
        /// User ID of the vault creator.
        /// </summary>
        public string CreatedBy { get; set; } = string.Empty;

        /// <summary>
        /// When the vault was created.
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// Number of times keys have been rotated.
        /// </summary>
        public int KeyRotationCount { get; set; }

        /// <summary>
        /// Last time keys were rotated.
        /// </summary>
        public DateTime? LastKeyRotation { get; set; }

        /// <summary>
        /// Version of UvfLib.Net that created this vault.
        /// </summary>
        public string? CreatedByVersion { get; set; }

        /// <summary>
        /// Creates new multi-user vault metadata.
        /// </summary>
        /// <param name="createdBy">User ID of the creator</param>
        public MultiUserVaultMetadata(string createdBy)
        {
            CreatedBy = createdBy ?? throw new ArgumentNullException(nameof(createdBy));
            CreatedAt = DateTime.UtcNow;
            KeyRotationCount = 0;
            CreatedByVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString();
        }

        /// <summary>
        /// Parameterless constructor for serialization.
        /// </summary>
        public MultiUserVaultMetadata()
        {
        }

        /// <summary>
        /// Gets a user by their ID.
        /// </summary>
        /// <param name="userId">User ID to find</param>
        /// <returns>VaultUser if found, null otherwise</returns>
        public VaultUser? GetUser(string userId)
        {
            return Users.FirstOrDefault(u => u.UserId == userId);
        }

        /// <summary>
        /// Adds a user to the vault metadata.
        /// </summary>
        /// <param name="user">User to add</param>
        /// <exception cref="InvalidOperationException">If user already exists</exception>
        public void AddUser(VaultUser user)
        {
            if (user == null) throw new ArgumentNullException(nameof(user));
            
            if (GetUser(user.UserId) != null)
            {
                throw new InvalidOperationException($"User '{user.UserId}' already exists in vault");
            }

            Users.Add(user);
        }

        /// <summary>
        /// Removes a user from the vault metadata.
        /// </summary>
        /// <param name="userId">User ID to remove</param>
        /// <returns>True if user was removed, false if not found</returns>
        public bool RemoveUser(string userId)
        {
            var user = GetUser(userId);
            if (user != null)
            {
                Users.Remove(user);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Records a key rotation event.
        /// </summary>
        public void RecordKeyRotation()
        {
            KeyRotationCount++;
            LastKeyRotation = DateTime.UtcNow;
        }

        /// <summary>
        /// Gets all admin users.
        /// </summary>
        /// <returns>List of admin users</returns>
        public List<VaultUser> GetAdminUsers()
        {
            return Users.Where(u => u.Role == VaultUserRole.Admin).ToList();
        }

        /// <summary>
        /// Gets all regular users.
        /// </summary>
        /// <returns>List of regular users</returns>
        public List<VaultUser> GetRegularUsers()
        {
            return Users.Where(u => u.Role == VaultUserRole.User).ToList();
        }
    }
} 