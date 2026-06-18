#ifndef TITAN_VAULT_H
#define TITAN_VAULT_H

/*
 * =============================================================================
 * TitanVault - Native C API for UVF and Cryptomator Vault Operations
 * =============================================================================
 *
 * This header is the canonical C ABI contract for the TitanVault native library
 * (UVF.NET). It is consumed directly by C/C++ and as the input for Rust
 * (bindgen), Go (cgo), and Swift (module map) bindings. Every signature here
 * matches the native exports exactly.
 *
 * Conventions:
 *   - Strings cross the boundary as a UTF-8 byte pointer plus an explicit byte
 *     length (never assumed to be NUL-terminated on input). The companion
 *     "*_length" parameter is the number of bytes, not characters.
 *   - Functions returning int: 0 (TITAN_VAULT_SUCCESS) means success; a negative
 *     value is an error code. Some functions (file_exists, directory_exists,
 *     list_directory, get_vault_users, stream_read, stream_write) return a
 *     non-negative count/flag on success instead. Call
 *     titan_vault_get_last_error() for a human-readable message after a failure.
 *   - Functions returning char*: the result is a heap-allocated, NUL-terminated
 *     UTF-8 string owned by the caller. Free it with titan_vault_free_string().
 *     The same applies to every per-entry string produced by
 *     titan_vault_list_directory() and titan_vault_get_vault_users() (each
 *     element of the char* array must be freed individually).
 *   - Functions returning TitanVaultHandle: NULL indicates failure. A vault
 *     handle is released with titan_vault_close_vault(); a stream handle with
 *     titan_vault_close_stream().
 *   - Pointer parameters named "*_size" / "*_buffer_size" are in/out: on input
 *     they hold the caller's buffer capacity; on output the required size.
 * =============================================================================
 */

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/* =============================================================================
 * Return codes
 * ========================================================================== */
#define TITAN_VAULT_SUCCESS                    0
#define TITAN_VAULT_ERROR_INVALID_PARAMETER   -1
#define TITAN_VAULT_ERROR_VAULT_NOT_FOUND     -2
#define TITAN_VAULT_ERROR_INVALID_PASSWORD    -3
#define TITAN_VAULT_ERROR_ACCESS_DENIED       -4
#define TITAN_VAULT_ERROR_VAULT_CORRUPTED     -5
#define TITAN_VAULT_ERROR_INSUFFICIENT_BUFFER -6
#define TITAN_VAULT_ERROR_UNSUPPORTED_FORMAT  -7
#define TITAN_VAULT_ERROR_INTERNAL            -100

/* Vault format constants (return value of titan_vault_detect_vault_format) */
#define TITAN_VAULT_FORMAT_CRYPTOMATOR         0
#define TITAN_VAULT_FORMAT_UVF                 1

/* KDF method constants (kdf_method argument of titan_vault_create_uvf_vault) */
#define TITAN_VAULT_KDF_PBKDF2                 0
#define TITAN_VAULT_KDF_SCRYPT                 1

/* Open flags (open_flags argument of titan_vault_open_stream_with_flags).
 * Mirrors StorageLib.Abstractions.OpenFlags; combine with bitwise OR. */
#define TITAN_VAULT_O_RDONLY              0x0000  /* Open for read-only access */
#define TITAN_VAULT_O_WRONLY              0x0001  /* Open for write-only access */
#define TITAN_VAULT_O_RDWR                0x0002  /* Open for reading and writing */
#define TITAN_VAULT_O_CREAT               0x0040  /* Create file if it doesn't exist */
#define TITAN_VAULT_O_EXCL                0x0080  /* With O_CREAT, fail if file exists */
#define TITAN_VAULT_O_TRUNC               0x0200  /* Truncate file to zero length if it exists */
#define TITAN_VAULT_O_APPEND              0x0400  /* Open the file in append mode */

/* Seek origin constants (origin argument of titan_vault_stream_seek) */
#define TITAN_VAULT_SEEK_BEGIN                 0  /* Offset from the start of the stream */
#define TITAN_VAULT_SEEK_CURRENT               1  /* Offset from the current position */
#define TITAN_VAULT_SEEK_END                   2  /* Offset from the end of the stream */

/* Opaque handle type for vault and stream operations. */
typedef void* TitanVaultHandle;

/* =============================================================================
 * Utility
 * ========================================================================== */

/**
 * Get the TitanVault library version.
 * @return Heap-allocated, NUL-terminated UTF-8 string; free with
 *         titan_vault_free_string(). NULL on error.
 */
char* titan_vault_get_version(void);

/**
 * Get the last error message recorded on the current thread/process.
 * @return Heap-allocated, NUL-terminated UTF-8 string; free with
 *         titan_vault_free_string(). NULL on error.
 */
char* titan_vault_get_last_error(void);

/**
 * Detect the vault format at the specified path.
 * @param vault_path_bytes  UTF-8 vault path (input).
 * @param vault_path_length Length of the vault path in bytes.
 * @return A TITAN_VAULT_FORMAT_* constant, or a negative error code.
 */
int titan_vault_detect_vault_format(
    const unsigned char* vault_path_bytes,
    int vault_path_length);

/* =============================================================================
 * Cryptomator V8 Vault Operations
 * ========================================================================== */

/**
 * Create a new Cryptomator V8 vault.
 * @return TITAN_VAULT_SUCCESS or a negative error code.
 */
int titan_vault_create_cryptomator_vault(
    const unsigned char* vault_path_bytes,
    int vault_path_length,
    const unsigned char* password_bytes,
    int password_length);

/**
 * Load an existing Cryptomator V8 vault.
 * @return Vault handle (close with titan_vault_close_vault) or NULL on error.
 */
TitanVaultHandle titan_vault_load_cryptomator_vault(
    const unsigned char* vault_path_bytes,
    int vault_path_length,
    const unsigned char* password_bytes,
    int password_length);

/**
 * Change a Cryptomator vault's password.
 * @return TITAN_VAULT_SUCCESS or a negative error code.
 */
int titan_vault_change_cryptomator_password(
    const unsigned char* vault_path_bytes,
    int vault_path_length,
    const unsigned char* old_password_bytes,
    int old_password_length,
    const unsigned char* new_password_bytes,
    int new_password_length);

/* =============================================================================
 * UVF Vault Operations & User Management
 * ========================================================================== */

/**
 * Create a new UVF vault.
 * @param encrypt_filenames 1 to encrypt filenames, 0 to keep them plain.
 * @param kdf_method        A TITAN_VAULT_KDF_* constant.
 * @param kdf_iterations    PBKDF2 iteration count (ignored for Scrypt).
 * @return TITAN_VAULT_SUCCESS or a negative error code.
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
 * Load an existing UVF vault using a user password.
 * @param user_id_bytes  UTF-8 user ID (optional; pass NULL to auto-detect).
 * @param user_id_length Length of the user ID in bytes (0 if user_id_bytes is NULL).
 * @return Vault handle (close with titan_vault_close_vault) or NULL on error.
 */
TitanVaultHandle titan_vault_load_uvf_vault(
    const unsigned char* vault_path_bytes,
    int vault_path_length,
    const unsigned char* user_password_bytes,
    int user_password_length,
    const unsigned char* user_id_bytes,
    int user_id_length);

/**
 * Add a password-based user to a UVF vault (requires the admin password).
 * @return TITAN_VAULT_SUCCESS or a negative error code.
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
 * Remove a user from a UVF vault (requires the admin password).
 * @return TITAN_VAULT_SUCCESS or a negative error code.
 */
int titan_vault_remove_user(
    const unsigned char* vault_path_bytes,
    int vault_path_length,
    const unsigned char* admin_password_bytes,
    int admin_password_length,
    const unsigned char* user_id_to_remove_bytes,
    int user_id_to_remove_length);

/* =============================================================================
 * UVF Public-Key (Asymmetric) Multi-User Membership
 * ========================================================================== */

/**
 * Generate a user key pair (P-384) for public-key vault membership.
 *
 * Writes the public key (SubjectPublicKeyInfo) into public_key_buffer and the
 * password-encrypted PKCS#8 private key into private_key_buffer.
 *
 * @param public_key_buffer        Caller-allocated output buffer (may be NULL to query size).
 * @param public_key_buffer_size   In/out: input capacity, output required size (bytes).
 * @param private_key_buffer       Caller-allocated output buffer (may be NULL to query size).
 * @param private_key_buffer_size  In/out: input capacity, output required size (bytes).
 * @return TITAN_VAULT_SUCCESS; TITAN_VAULT_ERROR_INSUFFICIENT_BUFFER if a buffer
 *         is too small (the required sizes are written back); or a negative error code.
 */
int titan_vault_generate_user_keypair(
    const unsigned char* password_bytes,
    int password_length,
    unsigned char* public_key_buffer,
    int* public_key_buffer_size,
    unsigned char* private_key_buffer,
    int* private_key_buffer_size);

/**
 * Grant a user access to a UVF vault by their public key (SubjectPublicKeyInfo).
 * The admin password unwraps and re-wraps the vault key; the user's password is
 * not required.
 * @return TITAN_VAULT_SUCCESS or a negative error code.
 */
int titan_vault_add_user_by_public_key(
    const unsigned char* vault_path_bytes,
    int vault_path_length,
    const unsigned char* admin_password_bytes,
    int admin_password_length,
    const unsigned char* user_id_bytes,
    int user_id_length,
    const unsigned char* public_key_bytes,
    int public_key_length);

/**
 * Load a UVF vault using a user's password-encrypted (PKCS#8) private key.
 * @param user_id_bytes  UTF-8 user ID (optional; pass NULL to auto-detect).
 * @param user_id_length Length of the user ID in bytes (0 if user_id_bytes is NULL).
 * @return Vault handle (close with titan_vault_close_vault) or NULL on error.
 */
TitanVaultHandle titan_vault_load_uvf_vault_with_key(
    const unsigned char* vault_path_bytes,
    int vault_path_length,
    const unsigned char* encrypted_private_key_bytes,
    int encrypted_private_key_length,
    const unsigned char* key_password_bytes,
    int key_password_length,
    const unsigned char* user_id_bytes,
    int user_id_length);

/**
 * Rotate the vault key for a public-key membership: add a new seed and re-wrap
 * the fresh key to admin and every public-key member, without any member's
 * password. Fails if non-admin password recipients exist.
 * @return TITAN_VAULT_SUCCESS or a negative error code.
 */
int titan_vault_rotate_keys_pubkey(
    const unsigned char* vault_path_bytes,
    int vault_path_length,
    const unsigned char* admin_password_bytes,
    int admin_password_length);

/* =============================================================================
 * File Operations
 * ========================================================================== */

/**
 * Read a whole file from the vault into a caller-supplied buffer.
 *
 * @param buffer      Output buffer to receive file data. Pass NULL to query the
 *                    required size (returns TITAN_VAULT_ERROR_INSUFFICIENT_BUFFER
 *                    with the required size written to *buffer_size).
 * @param buffer_size In/out: input capacity, output the actual/required file
 *                    size in bytes.
 * @return TITAN_VAULT_SUCCESS, TITAN_VAULT_ERROR_INSUFFICIENT_BUFFER, or a
 *         negative error code.
 */
int titan_vault_read_file(
    TitanVaultHandle vault_handle,
    const unsigned char* file_path_bytes,
    int file_path_length,
    unsigned char* buffer,
    int* buffer_size);

/**
 * Write a whole file to the vault (overwriting any existing file).
 * @param buffer      File data to write (input).
 * @param buffer_size Number of bytes to write.
 * @return TITAN_VAULT_SUCCESS or a negative error code.
 */
int titan_vault_write_file(
    TitanVaultHandle vault_handle,
    const unsigned char* file_path_bytes,
    int file_path_length,
    const unsigned char* buffer,
    int buffer_size);

/**
 * Check whether a file exists in the vault.
 * @return 1 if it exists, 0 if not, or a negative error code.
 */
int titan_vault_file_exists(
    TitanVaultHandle vault_handle,
    const unsigned char* file_path_bytes,
    int file_path_length);

/**
 * Delete a file from the vault.
 * @return TITAN_VAULT_SUCCESS or a negative error code.
 */
int titan_vault_delete_file(
    TitanVaultHandle vault_handle,
    const unsigned char* file_path_bytes,
    int file_path_length);

/* =============================================================================
 * Text Helpers (UTF-8)
 * ========================================================================== */

/**
 * Read a text file from the vault as a UTF-8 string.
 * @return Heap-allocated, NUL-terminated UTF-8 string; free with
 *         titan_vault_free_string(). NULL on error.
 */
char* titan_vault_read_all_text(
    TitanVaultHandle vault_handle,
    const unsigned char* file_path_bytes,
    int file_path_length);

/**
 * Write a UTF-8 text string to a file in the vault (overwriting).
 * @return TITAN_VAULT_SUCCESS or a negative error code.
 */
int titan_vault_write_all_text(
    TitanVaultHandle vault_handle,
    const unsigned char* file_path_bytes,
    int file_path_length,
    const unsigned char* text_bytes,
    int text_length);

/**
 * Append a UTF-8 text string to a file in the vault.
 * @return TITAN_VAULT_SUCCESS or a negative error code.
 */
int titan_vault_append_all_text(
    TitanVaultHandle vault_handle,
    const unsigned char* file_path_bytes,
    int file_path_length,
    const unsigned char* text_bytes,
    int text_length);

/* =============================================================================
 * Directory Operations
 * ========================================================================== */

/**
 * Create a directory in the vault.
 * @return TITAN_VAULT_SUCCESS or a negative error code.
 */
int titan_vault_create_directory(
    TitanVaultHandle vault_handle,
    const unsigned char* directory_path_bytes,
    int directory_path_length);

/**
 * Check whether a directory exists in the vault.
 * @return 1 if it exists, 0 if not, or a negative error code.
 */
int titan_vault_directory_exists(
    TitanVaultHandle vault_handle,
    const unsigned char* directory_path_bytes,
    int directory_path_length);

/**
 * Delete a directory from the vault.
 * @return TITAN_VAULT_SUCCESS or a negative error code.
 */
int titan_vault_delete_directory(
    TitanVaultHandle vault_handle,
    const unsigned char* directory_path_bytes,
    int directory_path_length);

/**
 * List the contents of a directory in the vault.
 *
 * On success, fills entries_buffer with up to (*max_entries) heap-allocated,
 * NUL-terminated UTF-8 strings (one per entry). The caller allocates
 * entries_buffer as an array of at least (*max_entries) char* slots and must
 * free each returned element with titan_vault_free_string().
 *
 * @param entries_buffer Caller-allocated array of char* (output).
 * @param max_entries    In/out pointer: input is the array capacity; the number
 *                        of entries actually written is the function's return value.
 * @return Number of entries written (>= 0), or a negative error code.
 */
int titan_vault_list_directory(
    TitanVaultHandle vault_handle,
    const unsigned char* directory_path_bytes,
    int directory_path_length,
    char** entries_buffer,
    int* max_entries);

/**
 * Get file information (size and last-modified time).
 * @param file_size     Out: file size in bytes (may be NULL to skip).
 * @param last_modified Out: last-modified time as a Unix timestamp in seconds
 *                       (may be NULL to skip).
 * @return TITAN_VAULT_SUCCESS or a negative error code.
 */
int titan_vault_get_file_info(
    TitanVaultHandle vault_handle,
    const unsigned char* file_path_bytes,
    int file_path_length,
    int64_t* file_size,
    int64_t* last_modified);

/**
 * Move or rename a file or directory in the vault (overwrites the destination).
 * @return TITAN_VAULT_SUCCESS or a negative error code.
 */
int titan_vault_move(
    TitanVaultHandle vault_handle,
    const unsigned char* source_path_bytes,
    int source_path_length,
    const unsigned char* destination_path_bytes,
    int destination_path_length);

/* =============================================================================
 * Stream Operations
 * ========================================================================== */

/**
 * Open a file for reading as a stream.
 * @return Stream handle (close with titan_vault_close_stream) or NULL on error.
 */
TitanVaultHandle titan_vault_open_read_stream(
    TitanVaultHandle vault_handle,
    const unsigned char* file_path_bytes,
    int file_path_length);

/**
 * Open a file for writing as a stream.
 * @return Stream handle (close with titan_vault_close_stream) or NULL on error.
 */
TitanVaultHandle titan_vault_open_write_stream(
    TitanVaultHandle vault_handle,
    const unsigned char* file_path_bytes,
    int file_path_length);

/**
 * Open a file as a stream with specific flags.
 * @param open_flags Bitwise OR of TITAN_VAULT_O_* constants.
 * @return Stream handle (close with titan_vault_close_stream) or NULL on error.
 */
TitanVaultHandle titan_vault_open_stream_with_flags(
    TitanVaultHandle vault_handle,
    const unsigned char* file_path_bytes,
    int file_path_length,
    int open_flags);

/**
 * Read from a stream into a caller-supplied buffer.
 * @param buffer Output buffer of at least 'count' bytes.
 * @param count  Maximum number of bytes to read.
 * @return Number of bytes read (>= 0; 0 at end of stream), or a negative error code.
 */
int titan_vault_stream_read(
    TitanVaultHandle stream_handle,
    unsigned char* buffer,
    int count);

/**
 * Write to a stream from a caller-supplied buffer.
 * @param buffer Input data buffer.
 * @param count  Number of bytes to write.
 * @return Number of bytes written (>= 0), or a negative error code.
 */
int titan_vault_stream_write(
    TitanVaultHandle stream_handle,
    const unsigned char* buffer,
    int count);

/**
 * Seek to a position in the stream.
 * @param offset Byte offset relative to 'origin'.
 * @param origin A TITAN_VAULT_SEEK_* constant (0 = begin, 1 = current, 2 = end).
 * @return The new absolute position (>= 0), or a negative error code.
 */
int64_t titan_vault_stream_seek(
    TitanVaultHandle stream_handle,
    int64_t offset,
    int origin);

/**
 * Get the current position in the stream.
 * @return The current position (>= 0), or a negative error code.
 */
int64_t titan_vault_stream_get_position(TitanVaultHandle stream_handle);

/**
 * Get the total length of the stream.
 * @return The stream length in bytes (>= 0), or a negative error code.
 */
int64_t titan_vault_stream_get_length(TitanVaultHandle stream_handle);

/**
 * Set the length of a writable stream.
 * @return TITAN_VAULT_SUCCESS or a negative error code.
 */
int titan_vault_stream_set_length(
    TitanVaultHandle stream_handle,
    int64_t length);

/**
 * Flush any pending writes to the stream.
 * @return TITAN_VAULT_SUCCESS or a negative error code.
 */
int titan_vault_stream_flush(TitanVaultHandle stream_handle);

/**
 * Close a stream and free its resources.
 * @return TITAN_VAULT_SUCCESS or a negative error code.
 */
int titan_vault_close_stream(TitanVaultHandle stream_handle);

/* =============================================================================
 * Vault Handle Management
 * ========================================================================== */

/**
 * Close a vault and free its resources.
 * @return TITAN_VAULT_SUCCESS or a negative error code.
 */
int titan_vault_close_vault(TitanVaultHandle vault_handle);

/* =============================================================================
 * Memory Management
 * ========================================================================== */

/**
 * Free a string previously returned by TitanVault (get_version, get_last_error,
 * read_all_text, or any per-entry string from list_directory / get_vault_users).
 * @param string_ptr String pointer to free (NULL is a no-op).
 */
void titan_vault_free_string(char* string_ptr);

/**
 * Securely zero a memory buffer (e.g. one holding a password or key).
 * @param buffer Buffer to zero.
 * @param size   Size of the buffer in bytes.
 */
void titan_vault_secure_zero_memory(unsigned char* buffer, int size);

/* =============================================================================
 * Maintenance & Advanced Vault Operations (operate on the vault PATH;
 * the vault does not need to be open)
 * ========================================================================== */

/**
 * Get the list of users in a UVF vault (requires the admin password).
 *
 * On success, fills users_buffer with up to (*max_users) heap-allocated,
 * NUL-terminated UTF-8 user-ID strings. The caller allocates users_buffer as an
 * array of at least (*max_users) char* slots and must free each returned element
 * with titan_vault_free_string().
 *
 * @param users_buffer Caller-allocated array of char* (output).
 * @param max_users    In/out pointer: input is the array capacity; the number of
 *                      users actually written is the function's return value.
 * @return Number of users written (>= 0), or a negative error code.
 */
int titan_vault_get_vault_users(
    const unsigned char* vault_path_bytes,
    int vault_path_length,
    const unsigned char* admin_password_bytes,
    int admin_password_length,
    char** users_buffer,
    int* max_users);

/**
 * Change the UVF admin password.
 * @return TITAN_VAULT_SUCCESS or a negative error code.
 */
int titan_vault_change_uvf_admin_password(
    const unsigned char* vault_path_bytes,
    int vault_path_length,
    const unsigned char* old_password_bytes,
    int old_password_length,
    const unsigned char* new_password_bytes,
    int new_password_length);

/**
 * Change a UVF user's password (requires the admin password).
 * @return TITAN_VAULT_SUCCESS or a negative error code.
 */
int titan_vault_change_uvf_user_password(
    const unsigned char* vault_path_bytes,
    int vault_path_length,
    const unsigned char* admin_password_bytes,
    int admin_password_length,
    const unsigned char* user_id_bytes,
    int user_id_length,
    const unsigned char* new_user_password_bytes,
    int new_user_password_length);

/**
 * Rotate the vault encryption keys (requires the admin password).
 * @param vault_format A TITAN_VAULT_FORMAT_* constant indicating the vault type.
 * @return TITAN_VAULT_SUCCESS or a negative error code.
 */
int titan_vault_rotate_keys(
    const unsigned char* vault_path_bytes,
    int vault_path_length,
    const unsigned char* admin_password_bytes,
    int admin_password_length,
    int vault_format);

/**
 * Back up the vault files to a target directory.
 * @param overwrite_existing 1 to overwrite existing files, 0 to keep them.
 * @return TITAN_VAULT_SUCCESS or a negative error code.
 */
int titan_vault_backup_files(
    const unsigned char* vault_path_bytes,
    int vault_path_length,
    const unsigned char* backup_path_bytes,
    int backup_path_length,
    int overwrite_existing);

#ifdef __cplusplus
}
#endif

#endif /* TITAN_VAULT_H */
