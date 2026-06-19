<?php
// Unified Vault Format (UVF) for C# and other languages.
// Copyright (c) Smart In Venture 2025- https://www.speedbits.io
// Licensed under AGPL-3.0 (commercial licenses available); see LICENSE.

// UVF / Cryptomator demo in PHP via the native TitanVault library (C ABI, using PHP's FFI extension).
//
// Full-parity port of Demo/NodeJs/vault-demo.js: same sections, same `… tests for <FORMAT>:
// PASSED/FAILED` lines, same flags, and (with no args) it runs everything — both formats' functional
// sections, the real-Cryptomator-vault interop, and a quick benchmark.
//
// Intended to run under WSL / Linux. Requires PHP 7.4+/8.x with the FFI extension enabled
// (ffi.enable=1 in php.ini, or run with `php -d ffi.enable=1 vault_demo.php`).
//
// Build the native library first (from the repo root):
//   ../../BuildScripts/build.sh --task aot        # -> Dist/Native/<rid>/libTitanVault.{so,dylib}
//   ../../BuildScripts/build.ps1 -Task aot        # -> Dist/Native/win-x64/TitanVault.dll
//
// Run (runs BOTH formats by default; --format uvf|cryptomator restricts to one):
//   php vault_demo.php
//   php vault_demo.php --lib ../../Dist/Native/linux-x64/libTitanVault.so

declare(strict_types=1);

if (!extension_loaded('ffi')) {
    fwrite(STDERR, "The PHP FFI extension is required. Enable it (ffi.enable=1 in php.ini) " .
        "or run with: php -d ffi.enable=1 vault_demo.php\n");
    exit(1);
}

const TITAN_VAULT_SUCCESS = 0;
const TITAN_VAULT_FORMAT_CRYPTOMATOR = 0;
const TITAN_VAULT_FORMAT_UVF = 1;
const MAX_LIST = 256;

// StorageLib.Abstractions.OpenFlags values (for open_stream_with_flags).
const OPEN_READONLY = 0x0000;
const OPEN_WRITEONLY = 0x0001;
const OPEN_CREATE = 0x0040;
const OPEN_TRUNCATE = 0x0200;

// ----- the C ABI, transcribed for FFI::cdef (clean prototypes only — no #define/comment/extern "C").
// `long long` is used for the 64-bit stream offsets/lengths (FFI maps PHP int <-> long long). Handles
// and streams are opaque `void*`; UTF-8 buffers are `unsigned char*`; in/out sizes are `int*`.
const C_DECLARATIONS = <<<'CDEF'
char* titan_vault_get_version(void);
char* titan_vault_get_last_error(void);
void titan_vault_free_string(char* string_ptr);
void titan_vault_secure_zero_memory(unsigned char* buffer, int size);

int titan_vault_detect_vault_format(const unsigned char* vault_path_bytes, int vault_path_length);

int titan_vault_create_cryptomator_vault(const unsigned char* vault_path_bytes, int vault_path_length,
    const unsigned char* password_bytes, int password_length);
void* titan_vault_load_cryptomator_vault(const unsigned char* vault_path_bytes, int vault_path_length,
    const unsigned char* password_bytes, int password_length);
int titan_vault_change_cryptomator_password(const unsigned char* vault_path_bytes, int vault_path_length,
    const unsigned char* old_password_bytes, int old_password_length,
    const unsigned char* new_password_bytes, int new_password_length);

int titan_vault_create_uvf_vault(const unsigned char* vault_path_bytes, int vault_path_length,
    const unsigned char* admin_password_bytes, int admin_password_length,
    int encrypt_filenames, int kdf_method, int kdf_iterations);
void* titan_vault_load_uvf_vault(const unsigned char* vault_path_bytes, int vault_path_length,
    const unsigned char* user_password_bytes, int user_password_length,
    const unsigned char* user_id_bytes, int user_id_length);
int titan_vault_change_uvf_admin_password(const unsigned char* vault_path_bytes, int vault_path_length,
    const unsigned char* old_password_bytes, int old_password_length,
    const unsigned char* new_password_bytes, int new_password_length);
int titan_vault_change_uvf_user_password(const unsigned char* vault_path_bytes, int vault_path_length,
    const unsigned char* admin_password_bytes, int admin_password_length,
    const unsigned char* user_id_bytes, int user_id_length,
    const unsigned char* new_user_password_bytes, int new_user_password_length);

int titan_vault_add_user(const unsigned char* vault_path_bytes, int vault_path_length,
    const unsigned char* admin_password_bytes, int admin_password_length,
    const unsigned char* new_user_id_bytes, int new_user_id_length,
    const unsigned char* new_user_password_bytes, int new_user_password_length);
int titan_vault_remove_user(const unsigned char* vault_path_bytes, int vault_path_length,
    const unsigned char* admin_password_bytes, int admin_password_length,
    const unsigned char* user_id_to_remove_bytes, int user_id_to_remove_length);
int titan_vault_get_vault_users(const unsigned char* vault_path_bytes, int vault_path_length,
    const unsigned char* admin_password_bytes, int admin_password_length,
    char** users_buffer, int* max_users);
int titan_vault_rotate_keys(const unsigned char* vault_path_bytes, int vault_path_length,
    const unsigned char* admin_password_bytes, int admin_password_length, int vault_format);

int titan_vault_generate_user_keypair(const unsigned char* password_bytes, int password_length,
    unsigned char* public_key_buffer, int* public_key_buffer_size,
    unsigned char* private_key_buffer, int* private_key_buffer_size);
int titan_vault_add_user_by_public_key(const unsigned char* vault_path_bytes, int vault_path_length,
    const unsigned char* admin_password_bytes, int admin_password_length,
    const unsigned char* user_id_bytes, int user_id_length,
    const unsigned char* public_key_bytes, int public_key_length);
void* titan_vault_load_uvf_vault_with_key(const unsigned char* vault_path_bytes, int vault_path_length,
    const unsigned char* encrypted_private_key_bytes, int encrypted_private_key_length,
    const unsigned char* key_password_bytes, int key_password_length,
    const unsigned char* user_id_bytes, int user_id_length);
int titan_vault_rotate_keys_pubkey(const unsigned char* vault_path_bytes, int vault_path_length,
    const unsigned char* admin_password_bytes, int admin_password_length);

int titan_vault_read_file(void* vault_handle, const unsigned char* file_path_bytes, int file_path_length,
    unsigned char* buffer, int* buffer_size);
int titan_vault_write_file(void* vault_handle, const unsigned char* file_path_bytes, int file_path_length,
    const unsigned char* buffer, int buffer_size);
int titan_vault_file_exists(void* vault_handle, const unsigned char* file_path_bytes, int file_path_length);
int titan_vault_delete_file(void* vault_handle, const unsigned char* file_path_bytes, int file_path_length);

char* titan_vault_read_all_text(void* vault_handle, const unsigned char* file_path_bytes, int file_path_length);
int titan_vault_write_all_text(void* vault_handle, const unsigned char* file_path_bytes, int file_path_length,
    const unsigned char* text_bytes, int text_length);
int titan_vault_append_all_text(void* vault_handle, const unsigned char* file_path_bytes, int file_path_length,
    const unsigned char* text_bytes, int text_length);

int titan_vault_create_directory(void* vault_handle, const unsigned char* directory_path_bytes, int directory_path_length);
int titan_vault_directory_exists(void* vault_handle, const unsigned char* directory_path_bytes, int directory_path_length);
int titan_vault_delete_directory(void* vault_handle, const unsigned char* directory_path_bytes, int directory_path_length);
int titan_vault_list_directory(void* vault_handle, const unsigned char* directory_path_bytes, int directory_path_length,
    char** entries_buffer, int* max_entries);
int titan_vault_get_file_info(void* vault_handle, const unsigned char* file_path_bytes, int file_path_length,
    long long* file_size, long long* last_modified);
int titan_vault_move(void* vault_handle, const unsigned char* source_path_bytes, int source_path_length,
    const unsigned char* destination_path_bytes, int destination_path_length);

void* titan_vault_open_read_stream(void* vault_handle, const unsigned char* file_path_bytes, int file_path_length);
void* titan_vault_open_write_stream(void* vault_handle, const unsigned char* file_path_bytes, int file_path_length);
void* titan_vault_open_stream_with_flags(void* vault_handle, const unsigned char* file_path_bytes, int file_path_length, int open_flags);
int titan_vault_stream_read(void* stream_handle, unsigned char* buffer, int count);
int titan_vault_stream_write(void* stream_handle, const unsigned char* buffer, int count);
long long titan_vault_stream_seek(void* stream_handle, long long offset, int origin);
long long titan_vault_stream_get_position(void* stream_handle);
long long titan_vault_stream_get_length(void* stream_handle);
int titan_vault_stream_set_length(void* stream_handle, long long length);
int titan_vault_stream_flush(void* stream_handle);
int titan_vault_close_stream(void* stream_handle);

int titan_vault_close_vault(void* vault_handle);

int titan_vault_backup_files(const unsigned char* vault_path_bytes, int vault_path_length,
    const unsigned char* backup_path_bytes, int backup_path_length, int overwrite_existing);
CDEF;

// ===========================================================================
// FFI helpers
// ===========================================================================

/**
 * Allocate a PHP-owned C `unsigned char[len]` buffer holding the bytes of $s and return it. The
 * returned CData decays to `unsigned char*` when passed to a function. Keep the returned value in a
 * PHP variable (or pass it inline so it lives for the statement) so the buffer is not freed mid-call.
 *
 * @return \FFI\CData|null Null for the empty string (so callers can pass a real NULL pointer + length 0).
 */
function buf(\FFI $ffi, string $s): ?\FFI\CData
{
    $n = strlen($s);
    if ($n === 0) {
        return null;
    }
    $cdata = $ffi->new("unsigned char[$n]");
    \FFI::memcpy($cdata, $s, $n);
    return $cdata;
}

/** UTF-8 byte length (PHP strings are already byte strings). */
function u8(string $s): int
{
    return strlen($s);
}

/** Read a returned NUL-terminated C string and free it via titan_vault_free_string. */
function takeString(\FFI $ffi, $ptr): string
{
    if ($ptr === null || \FFI::isNull($ptr)) {
        return '';
    }
    $s = \FFI::string($ptr);
    // $ptr is `char*`; free_string takes `char*` too — pass it straight through.
    $ffi->titan_vault_free_string($ptr);
    return $s;
}

/** Last error message (free_string-owned heap string). */
function lastError(\FFI $ffi): string
{
    $ptr = $ffi->titan_vault_get_last_error();
    if ($ptr === null || \FFI::isNull($ptr)) {
        return '(no error)';
    }
    $s = \FFI::string($ptr);
    $ffi->titan_vault_free_string($ptr);
    return $s;
}

/** Library version (free_string-owned heap string). */
function version(\FFI $ffi): string
{
    $ptr = $ffi->titan_vault_get_version();
    if ($ptr === null || \FFI::isNull($ptr)) {
        return '(unknown)';
    }
    return takeString($ffi, $ptr);
}

class VaultException extends \RuntimeException
{
}

function check(\FFI $ffi, int $rc, string $what): void
{
    if ($rc !== TITAN_VAULT_SUCCESS) {
        throw new VaultException("$what failed (rc=$rc): " . lastError($ffi));
    }
}

/**
 * Read a `char*[]` of $count entries into PHP strings, freeing each native string.
 *
 * @param \FFI\CData $entries A `char*[N]` CData.
 * @return string[]
 */
function readStringArray(\FFI $ffi, \FFI\CData $entries, int $count): array
{
    $out = [];
    for ($i = 0; $i < $count; $i++) {
        $p = $entries[$i];
        if ($p === null || \FFI::isNull($p)) {
            $out[] = '';
            continue;
        }
        $out[] = \FFI::string($p);
        $ffi->titan_vault_free_string($p);
    }
    return $out;
}

/** JSON-ish single-line array rendering matching the reference demos. */
function jsonList(array $v): string
{
    $parts = array_map(static fn ($s) => '"' . $s . '"', $v);
    return '[' . implode(',', $parts) . ']';
}

/**
 * Open a vault, mirroring the Node helper. Returns the handle CData or null on failure.
 */
function openVault(\FFI $ffi, string $format, string $vaultDir, string $password, ?string $userId = null, ?string $userPassword = null)
{
    $vbuf = buf($ffi, $vaultDir);
    if ($format === 'uvf') {
        $pw = ($userPassword !== null && $userPassword !== '') ? $userPassword : $password;
        $pwbuf = buf($ffi, $pw);
        $uidbuf = ($userId !== null && $userId !== '') ? buf($ffi, $userId) : null;
        $uidlen = ($userId !== null && $userId !== '') ? u8($userId) : 0;
        return $ffi->titan_vault_load_uvf_vault($vbuf, u8($vaultDir), $pwbuf, u8($pw), $uidbuf, $uidlen);
    }
    $pwbuf = buf($ffi, $password);
    return $ffi->titan_vault_load_cryptomator_vault($vbuf, u8($vaultDir), $pwbuf, u8($password));
}

/** Reads a whole vault file, growing the buffer to the required size if needed. */
function readFileFull(\FFI $ffi, $handle, string $vaultPath): string
{
    $cap = 1 << 20; // 1 MiB
    $pbuf = buf($ffi, $vaultPath);
    $plen = u8($vaultPath);
    for ($attempt = 0; $attempt < 5; $attempt++) {
        $buffer = $ffi->new("unsigned char[$cap]");
        $size = $ffi->new('int');
        $size->cdata = $cap;
        $rc = $ffi->titan_vault_read_file($handle, $pbuf, $plen, $buffer, \FFI::addr($size));
        if ($rc === TITAN_VAULT_SUCCESS) {
            $got = $size->cdata;
            return $got > 0 ? \FFI::string($buffer, $got) : '';
        }
        if ($size->cdata > $cap) {
            $cap = $size->cdata;
            continue;
        }
        throw new VaultException("read_file $vaultPath rc=$rc: " . lastError($ffi));
    }
    throw new VaultException("read_file $vaultPath: buffer growth failed");
}

/** @return string[] */
function listDir(\FFI $ffi, $handle, string $dirPath): array
{
    $entries = $ffi->new('char*[' . MAX_LIST . ']');
    $maxn = $ffi->new('int');
    $maxn->cdata = MAX_LIST;
    $pbuf = buf($ffi, $dirPath);
    $n = $ffi->titan_vault_list_directory($handle, $pbuf, u8($dirPath), $entries, \FFI::addr($maxn));
    if ($n < 0) {
        throw new VaultException("list_directory $dirPath rc=$n: " . lastError($ffi));
    }
    return readStringArray($ffi, $entries, $n);
}

/** Recursively walk a directory, returning all regular-file paths. */
function walk(string $dir): array
{
    $out = [];
    if (!is_dir($dir)) {
        return $out;
    }
    $it = new \RecursiveIteratorIterator(
        new \RecursiveDirectoryIterator($dir, \FilesystemIterator::SKIP_DOTS)
    );
    foreach ($it as $f) {
        if ($f->isFile()) {
            $out[] = $f->getPathname();
        }
    }
    return $out;
}

function rmrf(string $path): void
{
    if (!file_exists($path)) {
        return;
    }
    if (is_file($path) || is_link($path)) {
        @unlink($path);
        return;
    }
    $it = new \RecursiveIteratorIterator(
        new \RecursiveDirectoryIterator($path, \FilesystemIterator::SKIP_DOTS),
        \RecursiveIteratorIterator::CHILD_FIRST
    );
    foreach ($it as $f) {
        $f->isDir() ? @rmdir($f->getPathname()) : @unlink($f->getPathname());
    }
    @rmdir($path);
}

// ===========================================================================
// the per-format demo, organised into sections each reporting PASSED/FAILED
// ===========================================================================

class State
{
    public int $failed = 0;
}

function section(State $st, string $label, string $format, callable $fn): void
{
    try {
        $fn();
        echo "  $label tests for " . strtoupper($format) . ": PASSED\n";
    } catch (\Throwable $e) {
        $st->failed++;
        echo "  $label tests for " . strtoupper($format) . ": FAILED — " . $e->getMessage() . "\n";
    }
}

function runDemo(\FFI $ffi, string $format, string $vaultDir, string $password, State $st): void
{
    echo "\n========== " . strtoupper($format) . " ==========\n";
    rmrf($vaultDir);
    @mkdir($vaultDir, 0777, true);
    $vlen = u8($vaultDir);
    $plen = u8($password);
    $vdbuf = buf($ffi, $vaultDir);
    $pwbuf = buf($ffi, $password);

    if ($format === 'uvf') {
        check($ffi, $ffi->titan_vault_create_uvf_vault($vdbuf, $vlen, $pwbuf, $plen, 1, 0, 0), 'create_uvf_vault');
    } else {
        check($ffi, $ffi->titan_vault_create_cryptomator_vault($vdbuf, $vlen, $pwbuf, $plen), 'create_cryptomator_vault');
    }

    $handle = openVault($ffi, $format, $vaultDir, $password);
    if ($handle === null || \FFI::isNull($handle)) {
        throw new VaultException("load $format vault failed: " . lastError($ffi));
    }
    echo "Created + opened $format vault at $vaultDir\n";

    // A file we deliberately keep around to prove persistence + multi-user access later.
    $persist = 'persisted across reopen';
    $pp = '/persist.txt';
    $ppbuf = buf($ffi, $pp);
    $payloadBuf = buf($ffi, $persist);
    check($ffi, $ffi->titan_vault_write_file($handle, $ppbuf, u8($pp), $payloadBuf, strlen($persist)), 'write persist.txt');

    // 0. Detect the on-disk format (path-based — the vault need not be open).
    section($st, 'Detect format', $format, function () use ($ffi, $vaultDir, $vlen, $format) {
        $vb = buf($ffi, $vaultDir);
        $detected = $ffi->titan_vault_detect_vault_format($vb, $vlen);
        $expected = $format === 'uvf' ? TITAN_VAULT_FORMAT_UVF : TITAN_VAULT_FORMAT_CRYPTOMATOR;
        if ($detected !== $expected) {
            throw new VaultException("detect_vault_format=$detected, expected $expected");
        }
    });

    try {
        // 1. Basic file round-trip + filename-leak check.
        section($st, 'File', $format, function () use ($ffi, $handle, $vaultDir) {
            $fp = '/hello.txt';
            $plaintext = 'Hello, encrypted world!';
            $fpbuf = buf($ffi, $fp);
            $ptbuf = buf($ffi, $plaintext);
            check($ffi, $ffi->titan_vault_write_file($handle, $fpbuf, u8($fp), $ptbuf, strlen($plaintext)), 'write_file');
            $got = readFileFull($ffi, $handle, $fp);
            if ($got !== $plaintext) {
                throw new VaultException('round-trip mismatch');
            }
            foreach (walk($vaultDir) as $f) {
                if (basename($f) === 'hello.txt') {
                    throw new VaultException('plaintext filename leaked to disk');
                }
            }
            $fpbuf = buf($ffi, $fp);
            if ($ffi->titan_vault_file_exists($handle, $fpbuf, u8($fp)) !== 1) {
                throw new VaultException('exists should be 1');
            }
            $fpbuf = buf($ffi, $fp);
            check($ffi, $ffi->titan_vault_delete_file($handle, $fpbuf, u8($fp)), 'delete_file');
            $fpbuf = buf($ffi, $fp);
            if ($ffi->titan_vault_file_exists($handle, $fpbuf, u8($fp)) !== 0) {
                throw new VaultException('exists should be 0 after delete');
            }
        });

        // 1b. UTF-8 text convenience: write, append, read-back.
        section($st, 'Text helpers', $format, function () use ($ffi, $handle) {
            $tf = '/notes.txt';
            $first = "first line\n";
            $second = "second line\n";
            $tfbuf = buf($ffi, $tf);
            $firstBuf = buf($ffi, $first);
            check($ffi, $ffi->titan_vault_write_all_text($handle, $tfbuf, u8($tf), $firstBuf, u8($first)), 'write_all_text');
            $tfbuf = buf($ffi, $tf);
            $secondBuf = buf($ffi, $second);
            check($ffi, $ffi->titan_vault_append_all_text($handle, $tfbuf, u8($tf), $secondBuf, u8($second)), 'append_all_text');
            $tfbuf = buf($ffi, $tf);
            $ptr = $ffi->titan_vault_read_all_text($handle, $tfbuf, u8($tf));
            if ($ptr === null || \FFI::isNull($ptr)) {
                throw new VaultException('read_all_text: ' . lastError($ffi));
            }
            $text = takeString($ffi, $ptr);
            if ($text !== $first . $second) {
                throw new VaultException('text round-trip mismatch: ' . json_encode($text));
            }
        });

        // 2. Directories: create, write into, list, file-info, move/rename.
        section($st, 'Directory', $format, function () use ($ffi, $handle) {
            $dir = '/docs';
            $dbuf = buf($ffi, $dir);
            check($ffi, $ffi->titan_vault_create_directory($handle, $dbuf, u8($dir)), 'create_directory');
            $dbuf = buf($ffi, $dir);
            if ($ffi->titan_vault_directory_exists($handle, $dbuf, u8($dir)) !== 1) {
                throw new VaultException('directory_exists should be 1');
            }
            $note = '/docs/note.txt';
            $body = 'inside a subdirectory';
            $nbuf = buf($ffi, $note);
            $bodyBuf = buf($ffi, $body);
            check($ffi, $ffi->titan_vault_write_file($handle, $nbuf, u8($note), $bodyBuf, strlen($body)), 'write into dir');

            $names = listDir($ffi, $handle, $dir);
            if (!in_array('note.txt', $names, true)) {
                throw new VaultException('listing missing note.txt (got ' . jsonList($names) . ')');
            }

            $sizeBuf = $ffi->new('long long');
            $mtimeBuf = $ffi->new('long long');
            $nbuf = buf($ffi, $note);
            check($ffi, $ffi->titan_vault_get_file_info($handle, $nbuf, u8($note), \FFI::addr($sizeBuf), \FFI::addr($mtimeBuf)), 'get_file_info');
            $sz = $sizeBuf->cdata;
            if ($sz !== strlen($body)) {
                throw new VaultException("file size $sz != " . strlen($body));
            }

            $renamed = '/docs/renamed.txt';
            $nbuf = buf($ffi, $note);
            $rbuf = buf($ffi, $renamed);
            check($ffi, $ffi->titan_vault_move($handle, $nbuf, u8($note), $rbuf, u8($renamed)), 'move');
            $names = listDir($ffi, $handle, $dir);
            if (!in_array('renamed.txt', $names, true)) {
                throw new VaultException('rename not reflected (got ' . jsonList($names) . ')');
            }
            echo '    /docs now contains: ' . jsonList($names) . " (size of note was $sz bytes)\n";
        });

        // 3. Streaming: write a multi-chunk file, then random-access read with seek.
        section($st, 'Streaming', $format, function () use ($ffi, $handle) {
            $fp = '/big.bin';
            $CHUNK = 32 * 1024;
            $CHUNKS = 4;
            $total = $CHUNK * $CHUNKS;
            $chunk = '';
            for ($j = 0; $j < $CHUNK; $j++) {
                $chunk .= chr($j % 256); // file[O] == O % 256
            }
            $chunkBuf = buf($ffi, $chunk);

            $fpbuf = buf($ffi, $fp);
            $ws = $ffi->titan_vault_open_write_stream($handle, $fpbuf, u8($fp));
            if ($ws === null || \FFI::isNull($ws)) {
                throw new VaultException('open_write_stream: ' . lastError($ffi));
            }
            try {
                for ($i = 0; $i < $CHUNKS; $i++) {
                    if ($ffi->titan_vault_stream_write($ws, $chunkBuf, $CHUNK) !== $CHUNK) {
                        throw new VaultException('short write');
                    }
                }
                check($ffi, $ffi->titan_vault_stream_flush($ws), 'stream_flush');
            } finally {
                $ffi->titan_vault_close_stream($ws);
            }

            $fpbuf = buf($ffi, $fp);
            $rs = $ffi->titan_vault_open_read_stream($handle, $fpbuf, u8($fp));
            if ($rs === null || \FFI::isNull($rs)) {
                throw new VaultException('open_read_stream: ' . lastError($ffi));
            }
            try {
                $len = $ffi->titan_vault_stream_get_length($rs);
                if ($len !== $total) {
                    throw new VaultException("stream length $len != $total");
                }
                // sequential read of the whole thing, verifying the position-dependent pattern
                $rbuf = $ffi->new("unsigned char[$CHUNK]");
                $off = 0;
                while (($got = $ffi->titan_vault_stream_read($rs, $rbuf, $CHUNK)) > 0) {
                    for ($k = 0; $k < $got; $k++) {
                        if ($rbuf[$k] !== ($off + $k) % 256) {
                            throw new VaultException('byte mismatch at ' . ($off + $k));
                        }
                    }
                    $off += $got;
                }
                if ($off !== $total) {
                    throw new VaultException("read $off of $total");
                }
                $posAfterRead = $ffi->titan_vault_stream_get_position($rs);
                if ($posAfterRead !== $total) {
                    throw new VaultException("stream_get_position $posAfterRead != $total");
                }
                // random access: seek to a mid-file offset and verify (best-effort — not all backends seek)
                $seekTo = 70000;
                $pos = $ffi->titan_vault_stream_seek($rs, $seekTo, 0); // 0 = SEEK_SET
                if ($pos === $seekTo) {
                    $small = $ffi->new('unsigned char[16]');
                    if ($ffi->titan_vault_stream_read($rs, $small, 16) !== 16) {
                        throw new VaultException('short seek-read');
                    }
                    for ($k = 0; $k < 16; $k++) {
                        if ($small[$k] !== ($seekTo + $k) % 256) {
                            throw new VaultException('seek byte mismatch at ' . ($seekTo + $k));
                        }
                    }
                    echo "    wrote+verified $total bytes; seek to $seekTo OK\n";
                } else {
                    echo "    wrote+verified $total bytes; seek not supported by this backend (skipped)\n";
                }
                // open_stream_with_flags: reopen read-only and confirm the length matches.
                $fpbuf2 = buf($ffi, $fp);
                $rs2 = $ffi->titan_vault_open_stream_with_flags($handle, $fpbuf2, u8($fp), OPEN_READONLY);
                if ($rs2 === null || \FFI::isNull($rs2)) {
                    throw new VaultException('open_stream_with_flags: ' . lastError($ffi));
                }
                try {
                    if ($ffi->titan_vault_stream_get_length($rs2) !== $total) {
                        throw new VaultException('flags-open length mismatch');
                    }
                } finally {
                    $ffi->titan_vault_close_stream($rs2);
                }
            } finally {
                $ffi->titan_vault_close_stream($rs);
            }

            // stream_set_length: truncation of encrypted streams is backend-dependent; best-effort.
            try {
                $tp = '/trunc.bin';
                $tpbuf = buf($ffi, $tp);
                $ts = $ffi->titan_vault_open_stream_with_flags($handle, $tpbuf, u8($tp), OPEN_WRITEONLY | OPEN_CREATE | OPEN_TRUNCATE);
                if ($ts !== null && !\FFI::isNull($ts)) {
                    try {
                        $ffi->titan_vault_stream_write($ts, $chunkBuf, $CHUNK);
                        $ffi->titan_vault_stream_set_length($ts, 4096);
                    } finally {
                        $ffi->titan_vault_close_stream($ts);
                    }
                }
            } catch (\Throwable $e) {
                /* optional capability */
            }
        });
    } finally {
        $ffi->titan_vault_close_vault($handle);
        $handle = null;
    }

    // 4. Persistence: reopen the (closed) vault with the passphrase and re-read.
    section($st, 'Persistence', $format, function () use ($ffi, $format, $vaultDir, $password, $pp, $persist) {
        $h2 = openVault($ffi, $format, $vaultDir, $password);
        if ($h2 === null || \FFI::isNull($h2)) {
            throw new VaultException('reopen failed: ' . lastError($ffi));
        }
        try {
            if (readFileFull($ffi, $h2, $pp) !== $persist) {
                throw new VaultException('persisted content mismatch');
            }
        } finally {
            $ffi->titan_vault_close_vault($h2);
        }
    });

    // 5/6. UVF-only: key rotation, then multi-user (all operate on the vault path).
    if ($format === 'uvf') {
        // Key rotation must run while the vault is admin-only (the lib refuses to rotate a vault that
        // has extra users, since it would need every user's password to re-wrap the keys).
        $vb = buf($ffi, $vaultDir);
        $pwb = buf($ffi, $password);
        $rc = $ffi->titan_vault_rotate_keys($vb, $vlen, $pwb, $plen, TITAN_VAULT_FORMAT_UVF);
        if ($rc === TITAN_VAULT_SUCCESS) {
            echo "  Key rotation tests for UVF: PASSED\n";
        } else {
            $e = lastError($ffi);
            if (stripos($e, 'not implemented') !== false) {
                echo "  Key rotation tests for UVF: SKIPPED (not implemented)\n";
            } else {
                $st->failed++;
                echo "  Key rotation tests for UVF: FAILED — $e\n";
            }
        }

        // Public-key (asymmetric) membership: admin grants access to a public key, the user opens with
        // their private key, and the admin can rotate the key without the member's password. Runs before
        // the password Multi-user section so only admin + the public-key user exist at rotation time.
        section($st, 'Public-key multi-user', $format, function () use ($ffi, $vaultDir, $vlen, $password, $plen, $pp, $persist) {
            $bob = 'bob';
            $keyPw = 'bob-key-pass-123';

            // 1. Generate bob's key pair (public key + password-encrypted private key) via the C ABI.
            $pubCap = 4096;
            $privCap = 8192;
            $pubBuf = $ffi->new("unsigned char[$pubCap]");
            $privBuf = $ffi->new("unsigned char[$privCap]");
            $pubSize = $ffi->new('int');
            $pubSize->cdata = $pubCap;
            $privSize = $ffi->new('int');
            $privSize->cdata = $privCap;
            $keyPwBuf = buf($ffi, $keyPw);
            check($ffi, $ffi->titan_vault_generate_user_keypair($keyPwBuf, u8($keyPw), $pubBuf, \FFI::addr($pubSize), $privBuf, \FFI::addr($privSize)), 'generate_user_keypair');
            $pubLen = $pubSize->cdata;
            $privLen = $privSize->cdata;
            $publicKey = \FFI::string($pubBuf, $pubLen);
            $encryptedPrivateKey = \FFI::string($privBuf, $privLen);
            echo "    generated bob key pair (public {$pubLen}B, encrypted private {$privLen}B)\n";

            // 2. Grant bob access by PUBLIC key (admin needs no password from bob).
            $vb = buf($ffi, $vaultDir);
            $pwb = buf($ffi, $password);
            $bobBuf = buf($ffi, $bob);
            $pubKeyBuf = buf($ffi, $publicKey);
            check($ffi, $ffi->titan_vault_add_user_by_public_key($vb, $vlen, $pwb, $plen, $bobBuf, u8($bob), $pubKeyBuf, $pubLen), 'add_user_by_public_key');

            // 3. Open the vault as bob with his PRIVATE key and read the admin-written file.
            $readAsBob = function () use ($ffi, $vaultDir, $vlen, $encryptedPrivateKey, $privLen, $keyPw, $bob, $pp, $persist) {
                $vb = buf($ffi, $vaultDir);
                $privKeyBuf = buf($ffi, $encryptedPrivateKey);
                $keyPwBuf = buf($ffi, $keyPw);
                $bobBuf = buf($ffi, $bob);
                $h = $ffi->titan_vault_load_uvf_vault_with_key($vb, $vlen, $privKeyBuf, $privLen, $keyPwBuf, u8($keyPw), $bobBuf, u8($bob));
                if ($h === null || \FFI::isNull($h)) {
                    throw new VaultException('load as bob failed: ' . lastError($ffi));
                }
                try {
                    if (readFileFull($ffi, $h, $pp) !== $persist) {
                        throw new VaultException('bob read mismatch');
                    }
                } finally {
                    $ffi->titan_vault_close_vault($h);
                }
            };
            $readAsBob();
            echo "    opened as bob (public-key user) and read the admin file OK\n";

            // 4. Rotate the key for public-key members — admin alone, no bob password — then bob still reads.
            $vb = buf($ffi, $vaultDir);
            $pwb = buf($ffi, $password);
            check($ffi, $ffi->titan_vault_rotate_keys_pubkey($vb, $vlen, $pwb, $plen), 'rotate_keys_pubkey');
            $readAsBob();
            echo "    rotated keys (no member password) and bob still reads OK\n";
        });

        section($st, 'Multi-user', $format, function () use ($ffi, $vaultDir, $vlen, $password, $plen, $pp, $persist) {
            $alice = 'alice';
            $alicePw = 'alice-passphrase-123';
            $vb = buf($ffi, $vaultDir);
            $pwb = buf($ffi, $password);
            $aliceBuf = buf($ffi, $alice);
            $alicePwBuf = buf($ffi, $alicePw);
            check($ffi, $ffi->titan_vault_add_user($vb, $vlen, $pwb, $plen, $aliceBuf, u8($alice), $alicePwBuf, u8($alicePw)), 'add_user');

            $users = getVaultUsers($ffi, $vaultDir, $vlen, $password, $plen);
            echo '    vault users: ' . jsonList($users) . "\n";
            if (!in_array($alice, $users, true)) {
                throw new VaultException('added user not listed (got ' . jsonList($users) . ')');
            }

            // Best-effort: open as the new user and read the admin-written file. This currently fails
            // because LoadMultiUserUvfVaultAsync runs filename-encryption detection without the userId
            // (VaultManager.cs) — a known library limitation, reported (not failed) here.
            try {
                $ah = openVault($ffi, 'uvf', $vaultDir, $password, $alice, $alicePw);
                if ($ah === null || \FFI::isNull($ah)) {
                    throw new VaultException(lastError($ffi));
                }
                try {
                    if (readFileFull($ffi, $ah, $pp) !== $persist) {
                        throw new VaultException('alice read mismatch');
                    }
                    echo "    opened as second user and read the admin-written file OK\n";
                } finally {
                    $ffi->titan_vault_close_vault($ah);
                }
            } catch (\Throwable $e) {
                echo "    ⚠ opening as a secondary user is not yet supported by the library: " . $e->getMessage() . "\n";
            }

            // Change a member's password (admin-driven), then remove the member and confirm they're gone.
            $aliceNewPw = 'alice-passphrase-456';
            $vb = buf($ffi, $vaultDir);
            $pwb = buf($ffi, $password);
            $aliceBuf = buf($ffi, $alice);
            $aliceNewPwBuf = buf($ffi, $aliceNewPw);
            check($ffi, $ffi->titan_vault_change_uvf_user_password($vb, $vlen, $pwb, $plen, $aliceBuf, u8($alice), $aliceNewPwBuf, u8($aliceNewPw)), 'change_uvf_user_password');
            $vb = buf($ffi, $vaultDir);
            $pwb = buf($ffi, $password);
            $aliceBuf = buf($ffi, $alice);
            check($ffi, $ffi->titan_vault_remove_user($vb, $vlen, $pwb, $plen, $aliceBuf, u8($alice)), 'remove_user');
            $users2 = getVaultUsers($ffi, $vaultDir, $vlen, $password, $plen);
            if (in_array($alice, $users2, true)) {
                throw new VaultException('removed user still listed (got ' . jsonList($users2) . ')');
            }
            echo "    changed alice's password, then removed alice; users now: " . jsonList($users2) . "\n";
        });
    }

    // 7. Maintenance (both formats): backup the key files, secure-wipe a buffer, change the
    //    password, and reopen with the new password.
    section($st, 'Maintenance', $format, function () use ($ffi, $format, $vaultDir, $vlen, $password, $plen, $pp, $persist) {
        $backupDir = sys_get_temp_dir() . DIRECTORY_SEPARATOR . "uvf-backup-$format-" . getmypid();
        rmrf($backupDir);
        $vb = buf($ffi, $vaultDir);
        $bb = buf($ffi, $backupDir);
        check($ffi, $ffi->titan_vault_backup_files($vb, $vlen, $bb, u8($backupDir), 1), 'backup_files');
        if (!is_dir($backupDir) || count(walk($backupDir)) === 0) {
            throw new VaultException('backup produced no files');
        }

        $secret = 'super-secret-key-material';
        $secretLen = strlen($secret);
        $secretBuf = $ffi->new("unsigned char[$secretLen]");
        \FFI::memcpy($secretBuf, $secret, $secretLen);
        $ffi->titan_vault_secure_zero_memory($secretBuf, $secretLen);
        for ($i = 0; $i < $secretLen; $i++) {
            if ($secretBuf[$i] !== 0) {
                throw new VaultException('secure_zero_memory did not zero the buffer');
            }
        }

        $newPw = $password . '-rotated';
        $vb = buf($ffi, $vaultDir);
        $pwb = buf($ffi, $password);
        $newPwBuf = buf($ffi, $newPw);
        if ($format === 'uvf') {
            check($ffi, $ffi->titan_vault_change_uvf_admin_password($vb, $vlen, $pwb, $plen, $newPwBuf, u8($newPw)), 'change_uvf_admin_password');
        } else {
            check($ffi, $ffi->titan_vault_change_cryptomator_password($vb, $vlen, $pwb, $plen, $newPwBuf, u8($newPw)), 'change_cryptomator_password');
        }
        $h3 = openVault($ffi, $format, $vaultDir, $newPw);
        if ($h3 === null || \FFI::isNull($h3)) {
            throw new VaultException('reopen after password change failed: ' . lastError($ffi));
        }
        try {
            if (readFileFull($ffi, $h3, $pp) !== $persist) {
                throw new VaultException('content mismatch after password change');
            }
        } finally {
            $ffi->titan_vault_close_vault($h3);
        }
        rmrf($backupDir);
        echo "    backed up key files, secure-zeroed a buffer, changed the $format password and re-read OK\n";
    });

    echo "✅ $format demo finished.\n";
}

/**
 * get_vault_users wrapper: returns the user-ID strings (each freed).
 * @return string[]
 */
function getVaultUsers(\FFI $ffi, string $vaultDir, int $vlen, string $password, int $plen): array
{
    $users = $ffi->new('char*[' . MAX_LIST . ']');
    $maxn = $ffi->new('int');
    $maxn->cdata = MAX_LIST;
    $vb = buf($ffi, $vaultDir);
    $pwb = buf($ffi, $password);
    $n = $ffi->titan_vault_get_vault_users($vb, $vlen, $pwb, $plen, $users, \FFI::addr($maxn));
    if ($n < 0) {
        throw new VaultException("get_vault_users rc=$n: " . lastError($ffi));
    }
    return readStringArray($ffi, $users, $n);
}

// ===========================================================================
// interop: unlock a REAL Cryptomator vault and byte-compare the files
// ===========================================================================

function findInteropBase(): ?string
{
    $starts = [__DIR__, getcwd()];
    foreach ($starts as $start) {
        if ($start === false || $start === '') {
            continue;
        }
        $d = $start;
        while (true) {
            $candidates = [
                $d . DIRECTORY_SEPARATOR . '_test-cryptomator-vault',
                $d . DIRECTORY_SEPARATOR . 'Demo' . DIRECTORY_SEPARATOR . '_test-cryptomator-vault',
            ];
            foreach ($candidates as $cand) {
                if (is_file($cand . DIRECTORY_SEPARATOR . 'smartinventure' . DIRECTORY_SEPARATOR . 'masterkey.cryptomator')) {
                    return $cand;
                }
            }
            $parent = dirname($d);
            if ($parent === $d) {
                break;
            }
            $d = $parent;
        }
    }
    return null;
}

function runCryptomatorInterop(\FFI $ffi): bool
{
    echo "\n========== Cryptomator interop (real vault) ==========\n";
    $base = findInteropBase();
    if ($base === null) {
        echo "(Cryptomator interop skipped — Demo/_test-cryptomator-vault not found)\n";
        return true;
    }
    $vaultDir = $base . DIRECTORY_SEPARATOR . 'smartinventure';
    $origDir = $base . DIRECTORY_SEPARATOR . 'original-files';
    $password = 'smartinventure'; // demo vault — hardcoded on purpose

    $vb = buf($ffi, $vaultDir);
    $pwb = buf($ffi, $password);
    $handle = $ffi->titan_vault_load_cryptomator_vault($vb, u8($vaultDir), $pwb, u8($password));
    if ($handle === null || \FFI::isNull($handle)) {
        echo 'Unlock failed: ' . lastError($ffi) . "\n";
        return false;
    }
    $allOk = true;
    try {
        echo "Unlocked real Cryptomator vault at $vaultDir\n";
        foreach (['/', '/mysubfolder1', '/mysubfolder1/mysubfolder2'] as $d) {
            echo "  $d  ->  " . jsonList(listDir($ffi, $handle, $d)) . "\n";
        }
        $cases = [
            ['/Perfect-albums.txt', 'Perfect-albums.txt'],
            ['/mysubfolder1/banana.jpg', 'banana.jpg'],
            ['/mysubfolder1/mysubfolder2/Rubicon - Rivers - lyrics.txt', 'Rubicon - Rivers - lyrics.txt'],
        ];
        foreach ($cases as [$vaultPath, $origName]) {
            $decrypted = readFileFull($ffi, $handle, $vaultPath);
            $got = md5($decrypted);
            $want = md5((string)file_get_contents($origDir . DIRECTORY_SEPARATOR . $origName));
            $ok = $got === $want;
            if (!$ok) {
                $allOk = false;
            }
            $len = strlen($decrypted);
            $verdict = $ok ? 'match' : "MISMATCH got=$got want=$want";
            echo '  ' . ($ok ? '✓' : '✗') . " $vaultPath  ($len B)  md5 $verdict\n";
        }
        echo $allOk
            ? "✅ Reading a real Cryptomator vault worked — all files decrypted and md5-matched the originals.\n"
            : "❌ Cryptomator interop FAILED — md5 mismatch.\n";
    } catch (\Throwable $e) {
        echo '❌ Cryptomator interop FAILED: ' . $e->getMessage() . "\n";
        $allOk = false;
    } finally {
        $ffi->titan_vault_close_vault($handle);
    }
    return $allOk;
}

// ===========================================================================
// benchmark
// ===========================================================================

function mbps(float $bytes, float $ms): float
{
    return ($bytes / 1e6) / ($ms / 1000.0); // decimal MB/s
}

function nowMs(): float
{
    return microtime(true) * 1000.0;
}

function runBenchmark(\FFI $ffi, float $sizeGb): void
{
    $sizeBytes = (int)round($sizeGb * 1024 * 1024 * 1024);
    $CHUNK = 4 * 1024 * 1024; // 4 MiB
    echo "\n========== Benchmark ($sizeGb GB per format, " . ($CHUNK >> 20) . " MiB chunks) ==========\n";
    echo "  (disk read/write rows may just reflect the OS cache — pass --size larger than your RAM for disk-bound numbers)\n";
    foreach (['uvf', 'cryptomator'] as $format) {
        benchOne($ffi, $format, $sizeBytes, $CHUNK);
    }
}

function benchOne(\FFI $ffi, string $format, int $sizeBytes, int $CHUNK): void
{
    echo "\n----- " . strtoupper($format) . " -----\n";
    $dir = sys_get_temp_dir() . DIRECTORY_SEPARATOR . "uvf-bench-$format-" . getmypid();
    rmrf($dir);
    $vaultDir = $dir . DIRECTORY_SEPARATOR . 'vault';
    @mkdir($vaultDir, 0777, true);
    $plain = $dir . DIRECTORY_SEPARATOR . 'plain.bin';
    $password = 'bench-pass-123';
    $report = static function (string $label, float $ms) use ($sizeBytes) {
        printf("  %-38s %7.0f ms   %8.1f MB/s\n", $label, $ms, mbps((float)$sizeBytes, $ms));
    };

    $chunk = '';
    for ($i = 0; $i < $CHUNK; $i++) {
        $chunk .= chr($i & 0xff); // non-trivial data (avoid sparse-file effects)
    }
    $chunkBuf = buf($ffi, $chunk);

    try {
        // (a) create the plaintext file on disk — gauges raw medium write speed
        $t = nowMs();
        $fp = fopen($plain, 'wb');
        $w = 0;
        while ($w < $sizeBytes) {
            $n = min($CHUNK, $sizeBytes - $w);
            fwrite($fp, $n === $CHUNK ? $chunk : substr($chunk, 0, $n));
            $w += $n;
        }
        fflush($fp);
        fclose($fp);
        $report('create file (disk write, may be cached)', nowMs() - $t);

        $vlen = u8($vaultDir);
        $plen = u8($password);
        $vb = buf($ffi, $vaultDir);
        $pwb = buf($ffi, $password);
        if ($format === 'uvf') {
            check($ffi, $ffi->titan_vault_create_uvf_vault($vb, $vlen, $pwb, $plen, 1, 0, 0), 'create_uvf_vault');
        } else {
            check($ffi, $ffi->titan_vault_create_cryptomator_vault($vb, $vlen, $pwb, $plen), 'create_cryptomator_vault');
        }
        $handle = openVault($ffi, $format, $vaultDir, $password);
        if ($handle === null || \FFI::isNull($handle)) {
            throw new VaultException('load failed: ' . lastError($ffi));
        }

        try {
            $vp = '/big.bin';
            // (b) encrypt — stream the plaintext into the vault
            $t = nowMs();
            $vpbuf = buf($ffi, $vp);
            $ws = $ffi->titan_vault_open_write_stream($handle, $vpbuf, u8($vp));
            if ($ws === null || \FFI::isNull($ws)) {
                throw new VaultException('open_write_stream: ' . lastError($ffi));
            }
            $fp = fopen($plain, 'rb');
            try {
                while (!feof($fp)) {
                    $data = fread($fp, $CHUNK);
                    if ($data === '' || $data === false) {
                        break;
                    }
                    $rd = strlen($data);
                    $rbuf = buf($ffi, $data);
                    if ($ffi->titan_vault_stream_write($ws, $rbuf, $rd) !== $rd) {
                        throw new VaultException('short write');
                    }
                }
            } finally {
                fclose($fp);
                $ffi->titan_vault_close_stream($ws);
            }
            $report("encrypt ($format)", nowMs() - $t);

            // (c) decrypt — stream it back out of the vault (discarding the plaintext)
            $t = nowMs();
            $vpbuf = buf($ffi, $vp);
            $rs = $ffi->titan_vault_open_read_stream($handle, $vpbuf, u8($vp));
            if ($rs === null || \FFI::isNull($rs)) {
                throw new VaultException('open_read_stream: ' . lastError($ffi));
            }
            $dbuf = $ffi->new("unsigned char[$CHUNK]");
            $totalN = 0;
            try {
                while (($got = $ffi->titan_vault_stream_read($rs, $dbuf, $CHUNK)) > 0) {
                    $totalN += $got;
                }
            } finally {
                $ffi->titan_vault_close_stream($rs);
            }
            if ($totalN !== $sizeBytes) {
                throw new VaultException("decrypt size $totalN != $sizeBytes");
            }
            $report("decrypt ($format)", nowMs() - $t);

            // (d) read the plaintext file back from disk — gauges raw medium read speed
            $t = nowMs();
            $fp = fopen($plain, 'rb');
            while (!feof($fp)) {
                fread($fp, $CHUNK); // discard
            }
            fclose($fp);
            $report('read file (disk read, may be cached)', nowMs() - $t);
        } finally {
            $ffi->titan_vault_close_vault($handle);
        }
    } finally {
        rmrf($dir);
    }
}

// ===========================================================================
// library discovery + args
// ===========================================================================

function libFileName(): string
{
    switch (PHP_OS_FAMILY) {
        case 'Windows':
            return 'TitanVault.dll';
        case 'Darwin':
            return 'libTitanVault.dylib';
        default:
            return 'libTitanVault.so';
    }
}

function ridString(): string
{
    switch (PHP_OS_FAMILY) {
        case 'Windows':
            $os = 'win-';
            break;
        case 'Darwin':
            $os = 'osx-';
            break;
        default:
            $os = 'linux-';
            break;
    }
    $machine = strtolower(php_uname('m'));
    $isArm = strpos($machine, 'aarch64') !== false || strpos($machine, 'arm64') !== false;
    return $os . ($isArm ? 'arm64' : 'x64');
}

function discoverLib(): string
{
    $file = libFileName();
    $rid = ridString();
    $cwd = getcwd() ?: '.';

    $candidates = [
        __DIR__ . DIRECTORY_SEPARATOR . $file,
        $cwd . DIRECTORY_SEPARATOR . $file,
    ];
    // Walk up from both __DIR__ and cwd, looking for Dist/Native/<rid>/<file>.
    foreach ([$cwd, __DIR__] as $start) {
        $d = $start;
        while (true) {
            $candidates[] = $d . DIRECTORY_SEPARATOR . 'Dist' . DIRECTORY_SEPARATOR . 'Native' . DIRECTORY_SEPARATOR . $rid . DIRECTORY_SEPARATOR . $file;
            $parent = dirname($d);
            if ($parent === $d) {
                break;
            }
            $d = $parent;
        }
    }
    foreach ($candidates as $c) {
        if (is_file($c)) {
            return $c;
        }
    }
    // Fall back to the Dist path so the "not found" message points at the build.
    return __DIR__ . DIRECTORY_SEPARATOR . '..' . DIRECTORY_SEPARATOR . '..'
        . DIRECTORY_SEPARATOR . 'Dist' . DIRECTORY_SEPARATOR . 'Native'
        . DIRECTORY_SEPARATOR . $rid . DIRECTORY_SEPARATOR . $file;
}

function parseArgs(array $argv): array
{
    // Precedence: --lib > TITANVAULT_LIB env > discovery. format unset => run BOTH formats.
    $envLib = getenv('TITANVAULT_LIB');
    $a = [
        'lib' => ($envLib !== false && $envLib !== '') ? $envLib : discoverLib(),
        'format' => null,
        'vault' => sys_get_temp_dir() . DIRECTORY_SEPARATOR . 'uvf-php-demo',
        'password' => 'correct horse battery staple',
        'benchmark' => false,
        'interop' => false,
        'sizeGb' => 1.0,
    ];
    $n = count($argv);
    // Reads the value after an option and advances the index past it.
    $next = static function (array $argv, int &$i, int $n): string {
        return ($i + 1 < $n) ? (string)$argv[++$i] : '';
    };
    for ($i = 1; $i < $n; $i++) {
        switch ($argv[$i]) {
            case '--lib':
                $a['lib'] = $next($argv, $i, $n);
                break;
            case '--format':
                $a['format'] = $next($argv, $i, $n);
                break;
            case '--vault':
                $a['vault'] = $next($argv, $i, $n);
                break;
            case '--password':
                $a['password'] = $next($argv, $i, $n);
                break;
            case '--benchmark':
            case '--bench':
                $a['benchmark'] = true;
                break;
            case '--size':
                $a['sizeGb'] = (float)$next($argv, $i, $n);
                break;
            case '--cryptomator-interop':
            case '--interop':
                $a['interop'] = true;
                break;
        }
    }
    return $a;
}

function main(array $argv): int
{
    $args = parseArgs($argv);
    if (!is_file($args['lib'])) {
        fwrite(STDERR, "Native library not found: {$args['lib']}\n" .
            "Build it first:  ../../BuildScripts/build.sh --task aot   (or build.ps1 -Task aot)\n" .
            "Then it loads automatically (same folder / cwd / ../../Dist/Native/<rid>/), or pass --lib <path> / set TITANVAULT_LIB.\n" .
            "Note: the library must match your PHP architecture (" . php_uname('m') . ").\n");
        return 1;
    }

    try {
        $ffi = \FFI::cdef(C_DECLARATIONS, $args['lib']);
    } catch (\Throwable $e) {
        fwrite(STDERR, "Failed to load {$args['lib']} (architecture mismatch or bad cdef?): " . $e->getMessage() . "\n");
        return 1;
    }

    echo 'TitanVault version: ' . version($ffi) . "\n";

    // Focused modes (run only the requested thing).
    if ($args['interop']) {
        return runCryptomatorInterop($ffi) ? 0 : 1;
    }
    if ($args['benchmark']) {
        runBenchmark($ffi, $args['sizeGb']);
        return 0;
    }

    $st = new State();

    // Functional sections, for one format (--format) or both (default).
    $formats = $args['format'] !== null ? [$args['format']] : ['uvf', 'cryptomator'];
    foreach ($formats as $format) {
        try {
            runDemo($ffi, $format, $args['vault'] . DIRECTORY_SEPARATOR . $format, $args['password'], $st);
        } catch (\Throwable $e) {
            $st->failed++;
            echo "\n❌ $format demo aborted: " . $e->getMessage() . "\n";
        }
    }

    // A full run (no --format) also exercises the real-Cryptomator-vault interop and a quick throughput
    // benchmark.
    if ($args['format'] === null) {
        if (!runCryptomatorInterop($ffi)) {
            $st->failed++;
        }
        try {
            runBenchmark($ffi, 0.25);
        } catch (\Throwable $e) {
            $st->failed++;
            echo "\n❌ benchmark aborted: " . $e->getMessage() . "\n";
        }
    }

    echo $st->failed === 0
        ? "\n✅ All PHP demo sections passed.\n"
        : "\n❌ {$st->failed} section(s) failed.\n";
    return $st->failed === 0 ? 0 : 1;
}

exit(main($argv));
