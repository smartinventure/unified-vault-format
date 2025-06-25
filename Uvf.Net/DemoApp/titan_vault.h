#ifndef TITAN_VAULT_H
#define TITAN_VAULT_H

#ifdef __cplusplus
extern "C" {
#endif

// =============================================================================
// TitanVault - Native C API for UVF and Cryptomator Vault Operations
// =============================================================================

// Return codes
#define TITAN_VAULT_SUCCESS                    0
#define TITAN_VAULT_ERROR_INVALID_PARAMETER   -1
#define TITAN_VAULT_ERROR_VAULT_NOT_FOUND     -2
#define TITAN_VAULT_ERROR_INVALID_PASSWORD    -3
#define TITAN_VAULT_ERROR_ACCESS_DENIED       -4
#define TITAN_VAULT_ERROR_VAULT_CORRUPTED     -5
#define TITAN_VAULT_ERROR_INSUFFICIENT_BUFFER -6
#define TITAN_VAULT_ERROR_UNSUPPORTED_FORMAT  -7
#define TITAN_VAULT_ERROR_INTERNAL            -100

// Vault format constants
#define TITAN_VAULT_FORMAT_CRYPTOMATOR         0
#define TITAN_VAULT_FORMAT_UVF                 1

// KDF method constants
#define TITAN_VAULT_KDF_PBKDF2                 0
#define TITAN_VAULT_KDF_SCRYPT                 1

// Handle type for vault operations
typedef void* TitanVaultHandle;

// =============================================================================
// Utility Functions
// =============================================================================

/**
 * Get TitanVault library version.
 * @return Pointer to null-terminated UTF-8 string (must be freed with titan_vault_free_string)
 */
char* titan_vault_get_version(void);

/**
 * Get last error message.
 * @return Pointer to null-terminated UTF-8 string (must be freed with titan_vault_free_string)
 */
char* titan_vault_get_last_error(void);

/**
 * Detect vault format at specified path.
 * @param vault_path_bytes UTF-8 encoded vault path bytes
 * @param vault_path_length Length of vault path in bytes
 * @return Format constant (TITAN_VAULT_FORMAT_*) or negative error code
 */
int titan_vault_detect_vault_format(
    const unsigned char* vault_path_bytes,
    int vault_path_length);

// =============================================================================
// Cryptomator V8 Vault Operations
// =============================================================================

/**
 * Create new Cryptomator V8 vault.
 * @param vault_path_bytes UTF-8 encoded vault path bytes
 * @param vault_path_length Length of vault path in bytes
 * @param password_bytes UTF-8 encoded password bytes
 * @param password_length Length of password in bytes
 * @return TITAN_VAULT_SUCCESS or negative error code
 */
int titan_vault_create_cryptomator_vault(
    const unsigned char* vault_path_bytes,
    int vault_path_length,
    const unsigned char* password_bytes,
    int password_length);

/**
 * Load existing Cryptomator V8 vault.
 * @param vault_path_bytes UTF-8 encoded vault path bytes
 * @param vault_path_length Length of vault path in bytes
 * @param password_bytes UTF-8 encoded password bytes
 * @param password_length Length of password in bytes
 * @return Vault handle or NULL on error
 */
TitanVaultHandle titan_vault_load_cryptomator_vault(
    const unsigned char* vault_path_bytes,
    int vault_path_length,
    const unsigned char* password_bytes,
    int password_length);

/**
 * Change Cryptomator vault password.
 * @param vault_path_bytes UTF-8 encoded vault path bytes
 * @param vault_path_length Length of vault path in bytes
 * @param old_password_bytes UTF-8 encoded old password bytes
 * @param old_password_length Length of old password in bytes
 * @param new_password_bytes UTF-8 encoded new password bytes
 * @param new_password_length Length of new password in bytes
 * @return TITAN_VAULT_SUCCESS or negative error code
 */
int titan_vault_change_cryptomator_password(
    const unsigned char* vault_path_bytes,
    int vault_path_length,
    const unsigned char* old_password_bytes,
    int old_password_length,
    const unsigned char* new_password_bytes,
    int new_password_length);

// =============================================================================
// UVF Vault Operations
// =============================================================================

/**
 * Create new UVF vault.
 * @param vault_path_bytes UTF-8 encoded vault path bytes
 * @param vault_path_length Length of vault path in bytes
 * @param admin_password_bytes UTF-8 encoded admin password bytes
 * @param admin_password_length Length of admin password in bytes
 * @param encrypt_filenames 1 to encrypt filenames, 0 to keep them plain
 * @param kdf_method Key derivation method (TITAN_VAULT_KDF_*)
 * @param kdf_iterations KDF iterations (ignored for Scrypt, uses standard params)
 * @return TITAN_VAULT_SUCCESS or negative error code
 */
int titan_vault_create_uvf_vault(
    const unsigned char* vault_path_bytes,
    int vault_path_length,
    const unsigned char* admin_password_bytes,
    int admin_password_length,
    int encrypt_filenames,
    int kdf_method,
    int kdf_iterations);

/**
 * Load existing UVF vault.
 * @param vault_path_bytes UTF-8 encoded vault path bytes
 * @param vault_path_length Length of vault path in bytes
 * @param user_password_bytes UTF-8 encoded user password bytes
 * @param user_password_length Length of user password in bytes
 * @param user_id_bytes UTF-8 encoded user ID bytes (optional, can be NULL)
 * @param user_id_length Length of user ID in bytes (0 if user_id_bytes is NULL)
 * @return Vault handle or NULL on error
 */
TitanVaultHandle titan_vault_load_uvf_vault(
    const unsigned char* vault_path_bytes,
    int vault_path_length,
    const unsigned char* user_password_bytes,
    int user_password_length,
    const unsigned char* user_id_bytes,
    int user_id_length);

/**
 * Add user to UVF vault.
 * @param vault_path_bytes UTF-8 encoded vault path bytes
 * @param vault_path_length Length of vault path in bytes
 * @param admin_password_bytes UTF-8 encoded admin password bytes
 * @param admin_password_length Length of admin password in bytes
 * @param new_user_id_bytes UTF-8 encoded new user ID bytes
 * @param new_user_id_length Length of new user ID in bytes
 * @param new_user_password_bytes UTF-8 encoded new user password bytes
 * @param new_user_password_length Length of new user password in bytes
 * @return TITAN_VAULT_SUCCESS or negative error code
 */
int titan_vault_add_user(
    const unsigned char* vault_path_bytes,
    int vault_path_length,
    const unsigned char* admin_password_bytes,
    int admin_password_length,
    const unsigned char* new_user_id_bytes,
    int new_user_id_length,
    const unsigned char* new_user_password_bytes,
    int new_user_password_length);

/**
 * Remove user from UVF vault.
 * @param vault_path_bytes UTF-8 encoded vault path bytes
 * @param vault_path_length Length of vault path in bytes
 * @param admin_password_bytes UTF-8 encoded admin password bytes
 * @param admin_password_length Length of admin password in bytes
 * @param user_id_to_remove_bytes UTF-8 encoded user ID to remove bytes
 * @param user_id_to_remove_length Length of user ID to remove in bytes
 * @return TITAN_VAULT_SUCCESS or negative error code
 */
int titan_vault_remove_user(
    const unsigned char* vault_path_bytes,
    int vault_path_length,
    const unsigned char* admin_password_bytes,
    int admin_password_length,
    const unsigned char* user_id_to_remove_bytes,
    int user_id_to_remove_length);

// =============================================================================
// File Operations
// =============================================================================

/**
 * Read file from vault.
 * @param vault_handle Vault handle from load operation
 * @param file_path_bytes UTF-8 encoded file path bytes
 * @param file_path_length Length of file path in bytes
 * @param buffer Buffer to receive file data
 * @param buffer_size Pointer to buffer size (input: available, output: required)
 * @return TITAN_VAULT_SUCCESS or negative error code
 */
int titan_vault_read_file(
    TitanVaultHandle vault_handle,
    const unsigned char* file_path_bytes,
    int file_path_length,
    unsigned char* buffer,
    int* buffer_size);

/**
 * Write file to vault.
 * @param vault_handle Vault handle from load operation
 * @param file_path_bytes UTF-8 encoded file path bytes
 * @param file_path_length Length of file path in bytes
 * @param buffer File data to write
 * @param buffer_size Size of file data in bytes
 * @return TITAN_VAULT_SUCCESS or negative error code
 */
int titan_vault_write_file(
    TitanVaultHandle vault_handle,
    const unsigned char* file_path_bytes,
    int file_path_length,
    const unsigned char* buffer,
    int buffer_size);

/**
 * Check if file exists in vault.
 * @param vault_handle Vault handle from load operation
 * @param file_path_bytes UTF-8 encoded file path bytes
 * @param file_path_length Length of file path in bytes
 * @return 1 if exists, 0 if not, negative on error
 */
int titan_vault_file_exists(
    TitanVaultHandle vault_handle,
    const unsigned char* file_path_bytes,
    int file_path_length);

// =============================================================================
// Vault Handle Management
// =============================================================================

/**
 * Close vault and free resources.
 * @param vault_handle Vault handle to close
 * @return TITAN_VAULT_SUCCESS or negative error code
 */
int titan_vault_close_vault(TitanVaultHandle vault_handle);

// =============================================================================
// Memory Management
// =============================================================================

/**
 * Free string allocated by TitanVault.
 * @param string_ptr String pointer to free
 */
void titan_vault_free_string(char* string_ptr);

/**
 * Securely zero memory buffer.
 * @param buffer Buffer to zero
 * @param size Size of buffer in bytes
 */
void titan_vault_secure_zero_memory(unsigned char* buffer, int size);

// =============================================================================
// Usage Examples (C code)
// =============================================================================

/*
// Example 1: Create UVF vault with filename encryption
const char* vault_path = "/path/to/vault";
const char* admin_password = "secure_admin_password";

// Convert strings to UTF-8 byte arrays
const unsigned char* vault_path_bytes = (const unsigned char*)vault_path;
int vault_path_length = strlen(vault_path);
const unsigned char* password_bytes = (const unsigned char*)admin_password;
int password_length = strlen(admin_password);

int result = titan_vault_create_uvf_vault(
    vault_path_bytes, vault_path_length,
    password_bytes, password_length,
    1,  // encrypt_filenames = true
    TITAN_VAULT_KDF_PBKDF2,
    64000  // iterations
);

if (result != TITAN_VAULT_SUCCESS) {
    char* error = titan_vault_get_last_error();
    printf("Error: %s\n", error);
    titan_vault_free_string(error);
}

// Example 2: Load vault and read file
TitanVaultHandle vault = titan_vault_load_uvf_vault(
    vault_path_bytes, vault_path_length,
    password_bytes, password_length,
    NULL, 0  // no specific user ID
);

if (vault != NULL) {
    const char* file_path = "/document.txt";
    const unsigned char* file_path_bytes = (const unsigned char*)file_path;
    int file_path_length = strlen(file_path);
    
    unsigned char buffer[4096];
    int buffer_size = sizeof(buffer);
    
    int result = titan_vault_read_file(
        vault,
        file_path_bytes, file_path_length,
        buffer, &buffer_size
    );
    
    if (result == TITAN_VAULT_SUCCESS) {
        // File data is now in buffer, size is in buffer_size
        printf("Read %d bytes from file\n", buffer_size);
    }
    
    titan_vault_close_vault(vault);
}

// Example 3: Add user to vault
const char* new_user_id = "user123";
const char* new_user_password = "user_password";

const unsigned char* user_id_bytes = (const unsigned char*)new_user_id;
int user_id_length = strlen(new_user_id);
const unsigned char* user_password_bytes = (const unsigned char*)new_user_password;
int user_password_length = strlen(new_user_password);

int result = titan_vault_add_user(
    vault_path_bytes, vault_path_length,
    password_bytes, password_length,  // admin credentials
    user_id_bytes, user_id_length,
    user_password_bytes, user_password_length
);
*/

#ifdef __cplusplus
}
#endif

#endif // TITAN_VAULT_H 