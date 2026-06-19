// Unified Vault Format (UVF) for C# and other languages.
// Copyright (c) Smart In Venture 2025- https://www.speedbits.io
// Licensed under AGPL-3.0 (commercial licenses available); see LICENSE.

// UVF / Cryptomator demo in Rust via the native TitanVault library (C ABI, runtime-loaded with
// `libloading`).
//
// Full-parity port of Demo/NodeJs/vault-demo.js (and structurally mirrors Demo/Cpp/vault_demo.cpp):
// same sections, same `… tests for <FORMAT>: PASSED/FAILED` lines, same flags, and (with no args) it
// runs everything — both formats' functional sections, the real-Cryptomator-vault interop, and a
// quick benchmark.
//
// The library is loaded at RUNTIME (libloading::Library::new) — no link-time import lib is needed, so
// this works against any prebuilt TitanVault.{dll,so,dylib}. Each export is resolved into a typed
// function pointer whose signature matches the canonical header (Bindings/include/titan_vault.h).
//
// Build the native library first (from the repo root):
//   ../../BuildScripts/build.ps1 -Task aot        # -> Dist/Native/<rid>/TitanVault.dll
//   ../../BuildScripts/build.sh  --task aot        # -> Dist/Native/<rid>/libTitanVault.{so,dylib}
// Then:
//   cargo run                         # both formats + interop + benchmark
//   cargo run -- --format uvf         # one format's functional sections
//   cargo run -- --benchmark --size 2 # throughput only, 2 GB
//   cargo run -- --lib /path/to/TitanVault.dll

use std::ffi::{c_char, c_void, CStr};
use std::fs;
use std::io::{Read, Write};
use std::path::{Path, PathBuf};
use std::time::Instant;

use libloading::{Library, Symbol};

// ----- C ABI return codes / constants (from titan_vault.h) -----
const TITAN_VAULT_SUCCESS: i32 = 0;
const TITAN_VAULT_FORMAT_CRYPTOMATOR: i32 = 0;
const TITAN_VAULT_FORMAT_UVF: i32 = 1;
const TITAN_VAULT_SEEK_BEGIN: i32 = 0;
const MAX_LIST: usize = 256;

// OpenFlags (StorageLib.Abstractions) for open_stream_with_flags.
const OPEN_READONLY: i32 = 0x0000;
const OPEN_WRITEONLY: i32 = 0x0001;
const OPEN_CREATE: i32 = 0x0040;
const OPEN_TRUNCATE: i32 = 0x0200;

// Opaque vault / stream handle.
type Handle = *mut c_void;

// ----- one type alias per export, matching the header exactly -----
type FnGetVersion = unsafe extern "C" fn() -> *mut c_char;
type FnGetLastError = unsafe extern "C" fn() -> *mut c_char;
type FnFreeString = unsafe extern "C" fn(*mut c_char);
type FnDetectVaultFormat = unsafe extern "C" fn(*const u8, i32) -> i32;
type FnSecureZeroMemory = unsafe extern "C" fn(*mut u8, i32);
type FnBackupFiles = unsafe extern "C" fn(*const u8, i32, *const u8, i32, i32) -> i32;

type FnCreateCryptomator = unsafe extern "C" fn(*const u8, i32, *const u8, i32) -> i32;
type FnLoadCryptomator = unsafe extern "C" fn(*const u8, i32, *const u8, i32) -> Handle;
type FnChangeCryptomatorPassword =
    unsafe extern "C" fn(*const u8, i32, *const u8, i32, *const u8, i32) -> i32;

type FnCreateUvf = unsafe extern "C" fn(*const u8, i32, *const u8, i32, i32, i32, i32) -> i32;
type FnLoadUvf = unsafe extern "C" fn(*const u8, i32, *const u8, i32, *const u8, i32) -> Handle;
type FnChangeUvfAdminPassword =
    unsafe extern "C" fn(*const u8, i32, *const u8, i32, *const u8, i32) -> i32;
type FnChangeUvfUserPassword =
    unsafe extern "C" fn(*const u8, i32, *const u8, i32, *const u8, i32, *const u8, i32) -> i32;

type FnAddUser =
    unsafe extern "C" fn(*const u8, i32, *const u8, i32, *const u8, i32, *const u8, i32) -> i32;
type FnRemoveUser = unsafe extern "C" fn(*const u8, i32, *const u8, i32, *const u8, i32) -> i32;
type FnGetVaultUsers =
    unsafe extern "C" fn(*const u8, i32, *const u8, i32, *mut *mut c_char, *mut i32) -> i32;
type FnRotateKeys = unsafe extern "C" fn(*const u8, i32, *const u8, i32, i32) -> i32;

type FnGenerateUserKeypair =
    unsafe extern "C" fn(*const u8, i32, *mut u8, *mut i32, *mut u8, *mut i32) -> i32;
type FnAddUserByPublicKey =
    unsafe extern "C" fn(*const u8, i32, *const u8, i32, *const u8, i32, *const u8, i32) -> i32;
type FnLoadUvfWithKey =
    unsafe extern "C" fn(*const u8, i32, *const u8, i32, *const u8, i32, *const u8, i32) -> Handle;
type FnRotateKeysPubkey = unsafe extern "C" fn(*const u8, i32, *const u8, i32) -> i32;

type FnWriteFile = unsafe extern "C" fn(Handle, *const u8, i32, *const u8, i32) -> i32;
type FnReadFile = unsafe extern "C" fn(Handle, *const u8, i32, *mut u8, *mut i32) -> i32;
type FnFileExists = unsafe extern "C" fn(Handle, *const u8, i32) -> i32;
type FnDeleteFile = unsafe extern "C" fn(Handle, *const u8, i32) -> i32;
type FnMove = unsafe extern "C" fn(Handle, *const u8, i32, *const u8, i32) -> i32;

type FnWriteAllText = unsafe extern "C" fn(Handle, *const u8, i32, *const u8, i32) -> i32;
type FnAppendAllText = unsafe extern "C" fn(Handle, *const u8, i32, *const u8, i32) -> i32;
type FnReadAllText = unsafe extern "C" fn(Handle, *const u8, i32) -> *mut c_char;

type FnCreateDirectory = unsafe extern "C" fn(Handle, *const u8, i32) -> i32;
type FnDirectoryExists = unsafe extern "C" fn(Handle, *const u8, i32) -> i32;
type FnDeleteDirectory = unsafe extern "C" fn(Handle, *const u8, i32) -> i32;
type FnListDirectory = unsafe extern "C" fn(Handle, *const u8, i32, *mut *mut c_char, *mut i32) -> i32;
type FnGetFileInfo = unsafe extern "C" fn(Handle, *const u8, i32, *mut i64, *mut i64) -> i32;

type FnOpenReadStream = unsafe extern "C" fn(Handle, *const u8, i32) -> Handle;
type FnOpenWriteStream = unsafe extern "C" fn(Handle, *const u8, i32) -> Handle;
type FnOpenStreamWithFlags = unsafe extern "C" fn(Handle, *const u8, i32, i32) -> Handle;
type FnStreamRead = unsafe extern "C" fn(Handle, *mut u8, i32) -> i32;
type FnStreamWrite = unsafe extern "C" fn(Handle, *const u8, i32) -> i32;
type FnStreamSeek = unsafe extern "C" fn(Handle, i64, i32) -> i64;
type FnStreamGetPosition = unsafe extern "C" fn(Handle) -> i64;
type FnStreamGetLength = unsafe extern "C" fn(Handle) -> i64;
type FnStreamSetLength = unsafe extern "C" fn(Handle, i64) -> i32;
type FnStreamFlush = unsafe extern "C" fn(Handle) -> i32;
type FnCloseStream = unsafe extern "C" fn(Handle) -> i32;
type FnCloseVault = unsafe extern "C" fn(Handle) -> i32;

// ----- the resolved C ABI: one field per export -----
struct Api {
    // The library is kept alive for the program's lifetime; never dropped while symbols are in use.
    _lib: Library,

    get_version: FnGetVersion,
    get_last_error: FnGetLastError,
    free_string: FnFreeString,
    detect_vault_format: FnDetectVaultFormat,
    secure_zero_memory: FnSecureZeroMemory,
    backup_files: FnBackupFiles,

    create_cryptomator: FnCreateCryptomator,
    load_cryptomator: FnLoadCryptomator,
    change_cryptomator_password: FnChangeCryptomatorPassword,

    create_uvf: FnCreateUvf,
    load_uvf: FnLoadUvf,
    change_uvf_admin_password: FnChangeUvfAdminPassword,
    change_uvf_user_password: FnChangeUvfUserPassword,

    add_user: FnAddUser,
    remove_user: FnRemoveUser,
    get_vault_users: FnGetVaultUsers,
    rotate_keys: FnRotateKeys,

    generate_user_keypair: FnGenerateUserKeypair,
    add_user_by_public_key: FnAddUserByPublicKey,
    load_uvf_with_key: FnLoadUvfWithKey,
    rotate_keys_pubkey: FnRotateKeysPubkey,

    write_file: FnWriteFile,
    read_file: FnReadFile,
    file_exists: FnFileExists,
    delete_file: FnDeleteFile,
    move_: FnMove,

    write_all_text: FnWriteAllText,
    append_all_text: FnAppendAllText,
    read_all_text: FnReadAllText,

    create_directory: FnCreateDirectory,
    directory_exists: FnDirectoryExists,
    // Resolved for completeness (every export is bound); not exercised by the reference demo.
    #[allow(dead_code)]
    delete_directory: FnDeleteDirectory,
    list_directory: FnListDirectory,
    get_file_info: FnGetFileInfo,

    open_read_stream: FnOpenReadStream,
    open_write_stream: FnOpenWriteStream,
    open_stream_with_flags: FnOpenStreamWithFlags,
    stream_read: FnStreamRead,
    stream_write: FnStreamWrite,
    stream_seek: FnStreamSeek,
    stream_get_position: FnStreamGetPosition,
    stream_get_length: FnStreamGetLength,
    stream_set_length: FnStreamSetLength,
    stream_flush: FnStreamFlush,
    close_stream: FnCloseStream,
    close_vault: FnCloseVault,
}

// Resolve a single export, copying out the bare function pointer (so it no longer borrows `lib`).
unsafe fn sym<T: Copy>(lib: &Library, name: &[u8]) -> T {
    let s: Symbol<T> = lib
        .get(name)
        .unwrap_or_else(|e| panic!("missing export: {}: {e}", String::from_utf8_lossy(name)));
    *s
}

impl Api {
    fn load(path: &Path) -> Result<Api, String> {
        // SAFETY: loading an arbitrary shared library and trusting its declared exports.
        unsafe {
            let lib = Library::new(path)
                .map_err(|e| format!("Failed to load {} ({e})", path.display()))?;
            let api = Api {
                get_version: sym(&lib, b"titan_vault_get_version\0"),
                get_last_error: sym(&lib, b"titan_vault_get_last_error\0"),
                free_string: sym(&lib, b"titan_vault_free_string\0"),
                detect_vault_format: sym(&lib, b"titan_vault_detect_vault_format\0"),
                secure_zero_memory: sym(&lib, b"titan_vault_secure_zero_memory\0"),
                backup_files: sym(&lib, b"titan_vault_backup_files\0"),

                create_cryptomator: sym(&lib, b"titan_vault_create_cryptomator_vault\0"),
                load_cryptomator: sym(&lib, b"titan_vault_load_cryptomator_vault\0"),
                change_cryptomator_password: sym(&lib, b"titan_vault_change_cryptomator_password\0"),

                create_uvf: sym(&lib, b"titan_vault_create_uvf_vault\0"),
                load_uvf: sym(&lib, b"titan_vault_load_uvf_vault\0"),
                change_uvf_admin_password: sym(&lib, b"titan_vault_change_uvf_admin_password\0"),
                change_uvf_user_password: sym(&lib, b"titan_vault_change_uvf_user_password\0"),

                add_user: sym(&lib, b"titan_vault_add_user\0"),
                remove_user: sym(&lib, b"titan_vault_remove_user\0"),
                get_vault_users: sym(&lib, b"titan_vault_get_vault_users\0"),
                rotate_keys: sym(&lib, b"titan_vault_rotate_keys\0"),

                generate_user_keypair: sym(&lib, b"titan_vault_generate_user_keypair\0"),
                add_user_by_public_key: sym(&lib, b"titan_vault_add_user_by_public_key\0"),
                load_uvf_with_key: sym(&lib, b"titan_vault_load_uvf_vault_with_key\0"),
                rotate_keys_pubkey: sym(&lib, b"titan_vault_rotate_keys_pubkey\0"),

                write_file: sym(&lib, b"titan_vault_write_file\0"),
                read_file: sym(&lib, b"titan_vault_read_file\0"),
                file_exists: sym(&lib, b"titan_vault_file_exists\0"),
                delete_file: sym(&lib, b"titan_vault_delete_file\0"),
                move_: sym(&lib, b"titan_vault_move\0"),

                write_all_text: sym(&lib, b"titan_vault_write_all_text\0"),
                append_all_text: sym(&lib, b"titan_vault_append_all_text\0"),
                read_all_text: sym(&lib, b"titan_vault_read_all_text\0"),

                create_directory: sym(&lib, b"titan_vault_create_directory\0"),
                directory_exists: sym(&lib, b"titan_vault_directory_exists\0"),
                delete_directory: sym(&lib, b"titan_vault_delete_directory\0"),
                list_directory: sym(&lib, b"titan_vault_list_directory\0"),
                get_file_info: sym(&lib, b"titan_vault_get_file_info\0"),

                open_read_stream: sym(&lib, b"titan_vault_open_read_stream\0"),
                open_write_stream: sym(&lib, b"titan_vault_open_write_stream\0"),
                open_stream_with_flags: sym(&lib, b"titan_vault_open_stream_with_flags\0"),
                stream_read: sym(&lib, b"titan_vault_stream_read\0"),
                stream_write: sym(&lib, b"titan_vault_stream_write\0"),
                stream_seek: sym(&lib, b"titan_vault_stream_seek\0"),
                stream_get_position: sym(&lib, b"titan_vault_stream_get_position\0"),
                stream_get_length: sym(&lib, b"titan_vault_stream_get_length\0"),
                stream_set_length: sym(&lib, b"titan_vault_stream_set_length\0"),
                stream_flush: sym(&lib, b"titan_vault_stream_flush\0"),
                close_stream: sym(&lib, b"titan_vault_close_stream\0"),
                close_vault: sym(&lib, b"titan_vault_close_vault\0"),

                _lib: lib,
            };
            Ok(api)
        }
    }

    // ----- safe helpers wrapping the raw calls -----

    fn last_error(&self) -> String {
        unsafe {
            let p = (self.get_last_error)();
            if p.is_null() {
                return "(no error)".to_string();
            }
            let s = CStr::from_ptr(p).to_string_lossy().into_owned();
            (self.free_string)(p);
            s
        }
    }

    fn version(&self) -> String {
        unsafe {
            let p = (self.get_version)();
            if p.is_null() {
                return "(unknown)".to_string();
            }
            let s = CStr::from_ptr(p).to_string_lossy().into_owned();
            (self.free_string)(p);
            s
        }
    }

    fn check(&self, rc: i32, what: &str) -> Result<(), String> {
        if rc != TITAN_VAULT_SUCCESS {
            return Err(format!("{what} failed (rc={rc}): {}", self.last_error()));
        }
        Ok(())
    }

    // Decode `n` returned native strings, freeing each.
    fn read_string_array(&self, buf: &[*mut c_char], n: usize) -> Vec<String> {
        let mut out = Vec::with_capacity(n);
        unsafe {
            for &p in buf.iter().take(n) {
                if p.is_null() {
                    out.push(String::new());
                } else {
                    out.push(CStr::from_ptr(p).to_string_lossy().into_owned());
                    (self.free_string)(p);
                }
            }
        }
        out
    }

    fn list_dir(&self, handle: Handle, path: &str) -> Result<Vec<String>, String> {
        let pb = path.as_bytes();
        let mut entries: [*mut c_char; MAX_LIST] = [std::ptr::null_mut(); MAX_LIST];
        let mut maxn: i32 = MAX_LIST as i32;
        let n = unsafe {
            (self.list_directory)(handle, pb.as_ptr(), pb.len() as i32, entries.as_mut_ptr(), &mut maxn)
        };
        if n < 0 {
            return Err(format!("list_directory {path} rc={n}: {}", self.last_error()));
        }
        Ok(self.read_string_array(&entries, n as usize))
    }

    // Read a whole vault file, growing the buffer to the required size and retrying.
    fn read_file_full(&self, handle: Handle, path: &str) -> Result<Vec<u8>, String> {
        let pb = path.as_bytes();
        let mut cap: i32 = 1 << 20; // 1 MiB
        for _ in 0..5 {
            let mut buf = vec![0u8; cap as usize];
            let mut size: i32 = cap;
            let rc = unsafe {
                (self.read_file)(handle, pb.as_ptr(), pb.len() as i32, buf.as_mut_ptr(), &mut size)
            };
            if rc == TITAN_VAULT_SUCCESS {
                buf.truncate(size as usize);
                return Ok(buf);
            }
            if size > cap {
                cap = size; // grow to required size and retry
                continue;
            }
            return Err(format!("read_file {path} rc={rc}: {}", self.last_error()));
        }
        Err(format!("read_file {path}: buffer growth failed"))
    }

    fn open_vault(
        &self,
        format: &str,
        vault_dir: &str,
        password: &str,
        user_id: Option<&str>,
        user_password: Option<&str>,
    ) -> Handle {
        let vb = vault_dir.as_bytes();
        unsafe {
            if format == "uvf" {
                let pw = user_password.unwrap_or(password);
                let pwb = pw.as_bytes();
                let (uid_ptr, uid_len) = match user_id {
                    Some(u) => (u.as_bytes().as_ptr(), u.len() as i32),
                    None => (std::ptr::null(), 0),
                };
                (self.load_uvf)(vb.as_ptr(), vb.len() as i32, pwb.as_ptr(), pwb.len() as i32, uid_ptr, uid_len)
            } else {
                let pb = password.as_bytes();
                (self.load_cryptomator)(vb.as_ptr(), vb.len() as i32, pb.as_ptr(), pb.len() as i32)
            }
        }
    }
}

// ----- small free helpers -----
fn upper(s: &str) -> String {
    s.to_uppercase()
}
fn json_list(v: &[String]) -> String {
    let inner: Vec<String> = v.iter().map(|s| format!("\"{s}\"")).collect();
    format!("[{}]", inner.join(","))
}
fn mbps(bytes: f64, ms: f64) -> f64 {
    (bytes / 1e6) / (ms / 1000.0)
}
fn elapsed_ms(t: Instant) -> f64 {
    t.elapsed().as_secs_f64() * 1000.0
}
fn pid() -> u32 {
    std::process::id()
}

struct State {
    failed: u32,
}

// Run one section, printing the PASSED/FAILED line and counting failures.
fn section<F: FnOnce() -> Result<(), String>>(st: &mut State, label: &str, format: &str, fn_: F) {
    match fn_() {
        Ok(()) => println!("  {label} tests for {}: PASSED", upper(format)),
        Err(e) => {
            st.failed += 1;
            println!("  {label} tests for {}: FAILED — {e}", upper(format));
        }
    }
}

// Walk a directory tree, returning every regular file path.
fn walk(dir: &Path) -> Vec<PathBuf> {
    let mut out = Vec::new();
    if let Ok(entries) = fs::read_dir(dir) {
        for e in entries.flatten() {
            let p = e.path();
            if p.is_dir() {
                out.extend(walk(&p));
            } else {
                out.push(p);
            }
        }
    }
    out
}

fn leaked_to_disk(vault_dir: &Path, basename: &str) -> bool {
    walk(vault_dir)
        .iter()
        .any(|p| p.file_name().map(|n| n == basename).unwrap_or(false))
}

// ----- the per-format demo -----
fn run_demo(api: &Api, format: &str, vault_dir: &str, password: &str, st: &mut State) -> Result<(), String> {
    println!("\n========== {} ==========", upper(format));
    let vault_path = PathBuf::from(vault_dir);
    let _ = fs::remove_dir_all(&vault_path);
    fs::create_dir_all(&vault_path).map_err(|e| e.to_string())?;
    let vb = vault_dir.as_bytes();
    let pb = password.as_bytes();

    unsafe {
        if format == "uvf" {
            api.check(
                (api.create_uvf)(vb.as_ptr(), vb.len() as i32, pb.as_ptr(), pb.len() as i32, 1, 0, 0),
                "create_uvf_vault",
            )?;
        } else {
            api.check(
                (api.create_cryptomator)(vb.as_ptr(), vb.len() as i32, pb.as_ptr(), pb.len() as i32),
                "create_cryptomator_vault",
            )?;
        }
    }

    let handle = api.open_vault(format, vault_dir, password, None, None);
    if handle.is_null() {
        return Err(format!("load {format} vault failed: {}", api.last_error()));
    }
    println!("Created + opened {format} vault at {vault_dir}");

    // A file kept around to prove persistence + multi-user access later.
    let pp = "/persist.txt";
    let persist = b"persisted across reopen".to_vec();
    unsafe {
        api.check(
            (api.write_file)(handle, pp.as_bytes().as_ptr(), pp.len() as i32, persist.as_ptr(), persist.len() as i32),
            "write persist.txt",
        )?;
    }

    // 0. Detect format (path-based).
    section(st, "Detect format", format, || unsafe {
        let detected = (api.detect_vault_format)(vb.as_ptr(), vb.len() as i32);
        let expected = if format == "uvf" { TITAN_VAULT_FORMAT_UVF } else { TITAN_VAULT_FORMAT_CRYPTOMATOR };
        if detected != expected {
            return Err(format!("detect_vault_format={detected}, expected {expected}"));
        }
        Ok(())
    });

    // 1. File round-trip + filename-leak check.
    section(st, "File", format, || {
        let fp = "/hello.txt";
        let pt = b"Hello, encrypted world!".to_vec();
        unsafe {
            api.check(
                (api.write_file)(handle, fp.as_bytes().as_ptr(), fp.len() as i32, pt.as_ptr(), pt.len() as i32),
                "write_file",
            )?;
        }
        let got = api.read_file_full(handle, fp)?;
        if got != pt {
            return Err("round-trip mismatch".to_string());
        }
        if leaked_to_disk(&vault_path, "hello.txt") {
            return Err("plaintext filename leaked to disk".to_string());
        }
        unsafe {
            if (api.file_exists)(handle, fp.as_bytes().as_ptr(), fp.len() as i32) != 1 {
                return Err("exists should be 1".to_string());
            }
            api.check((api.delete_file)(handle, fp.as_bytes().as_ptr(), fp.len() as i32), "delete_file")?;
            if (api.file_exists)(handle, fp.as_bytes().as_ptr(), fp.len() as i32) != 0 {
                return Err("exists should be 0 after delete".to_string());
            }
        }
        Ok(())
    });

    // 1b. UTF-8 text convenience.
    section(st, "Text helpers", format, || {
        let tf = "/notes.txt";
        let first = "first line\n";
        let second = "second line\n";
        unsafe {
            api.check(
                (api.write_all_text)(handle, tf.as_bytes().as_ptr(), tf.len() as i32, first.as_bytes().as_ptr(), first.len() as i32),
                "write_all_text",
            )?;
            api.check(
                (api.append_all_text)(handle, tf.as_bytes().as_ptr(), tf.len() as i32, second.as_bytes().as_ptr(), second.len() as i32),
                "append_all_text",
            )?;
            let p = (api.read_all_text)(handle, tf.as_bytes().as_ptr(), tf.len() as i32);
            if p.is_null() {
                return Err(format!("read_all_text: {}", api.last_error()));
            }
            let text = CStr::from_ptr(p).to_string_lossy().into_owned();
            (api.free_string)(p);
            if text != format!("{first}{second}") {
                return Err(format!("text round-trip mismatch: {text:?}"));
            }
        }
        Ok(())
    });

    // 2. Directories: create, write into, list, file-info, move/rename.
    section(st, "Directory", format, || {
        let dir = "/docs";
        unsafe {
            api.check((api.create_directory)(handle, dir.as_bytes().as_ptr(), dir.len() as i32), "create_directory")?;
            if (api.directory_exists)(handle, dir.as_bytes().as_ptr(), dir.len() as i32) != 1 {
                return Err("directory_exists should be 1".to_string());
            }
        }
        let note = "/docs/note.txt";
        let body = b"inside a subdirectory".to_vec();
        unsafe {
            api.check(
                (api.write_file)(handle, note.as_bytes().as_ptr(), note.len() as i32, body.as_ptr(), body.len() as i32),
                "write into dir",
            )?;
        }
        let mut names = api.list_dir(handle, dir)?;
        if !names.iter().any(|n| n == "note.txt") {
            return Err(format!("listing missing note.txt (got {})", json_list(&names)));
        }
        let mut sz: i64 = 0;
        let mut mtime: i64 = 0;
        unsafe {
            api.check(
                (api.get_file_info)(handle, note.as_bytes().as_ptr(), note.len() as i32, &mut sz, &mut mtime),
                "get_file_info",
            )?;
        }
        if sz != body.len() as i64 {
            return Err(format!("file size {sz} != {}", body.len()));
        }
        let renamed = "/docs/renamed.txt";
        unsafe {
            api.check(
                (api.move_)(handle, note.as_bytes().as_ptr(), note.len() as i32, renamed.as_bytes().as_ptr(), renamed.len() as i32),
                "move",
            )?;
        }
        names = api.list_dir(handle, dir)?;
        if !names.iter().any(|n| n == "renamed.txt") {
            return Err(format!("rename not reflected (got {})", json_list(&names)));
        }
        println!("    /docs now contains: {} (size of note was {sz} bytes)", json_list(&names));
        Ok(())
    });

    // 3. Streaming: multi-chunk write, then random-access read; plus the fuller stream API.
    section(st, "Streaming", format, || {
        let fp = "/big.bin";
        const CHUNK: usize = 32 * 1024;
        const CHUNKS: usize = 4;
        let total: i64 = (CHUNK * CHUNKS) as i64;
        let mut chunk = vec![0u8; CHUNK];
        for (j, b) in chunk.iter_mut().enumerate() {
            *b = (j % 256) as u8; // file[O] == O % 256
        }

        unsafe {
            let ws = (api.open_write_stream)(handle, fp.as_bytes().as_ptr(), fp.len() as i32);
            if ws.is_null() {
                return Err(format!("open_write_stream: {}", api.last_error()));
            }
            for _ in 0..CHUNKS {
                if (api.stream_write)(ws, chunk.as_ptr(), CHUNK as i32) != CHUNK as i32 {
                    (api.close_stream)(ws);
                    return Err("short write".to_string());
                }
            }
            let flush_rc = (api.stream_flush)(ws);
            (api.close_stream)(ws);
            api.check(flush_rc, "stream_flush")?;

            let rs = (api.open_read_stream)(handle, fp.as_bytes().as_ptr(), fp.len() as i32);
            if rs.is_null() {
                return Err(format!("open_read_stream: {}", api.last_error()));
            }
            // Run the verification, always closing rs afterwards.
            let result = (|| -> Result<(), String> {
                if (api.stream_get_length)(rs) != total {
                    return Err("stream length mismatch".to_string());
                }
                let mut rbuf = vec![0u8; CHUNK];
                let mut off: i64 = 0;
                loop {
                    let got = (api.stream_read)(rs, rbuf.as_mut_ptr(), CHUNK as i32);
                    if got <= 0 {
                        break;
                    }
                    for k in 0..got as usize {
                        if rbuf[k] != ((off + k as i64) % 256) as u8 {
                            return Err(format!("byte mismatch at {}", off + k as i64));
                        }
                    }
                    off += got as i64;
                }
                if off != total {
                    return Err(format!("read {off} of {total}"));
                }
                if (api.stream_get_position)(rs) != total {
                    return Err("stream_get_position != total".to_string());
                }
                let seek_to: i64 = 70000;
                let pos = (api.stream_seek)(rs, seek_to, TITAN_VAULT_SEEK_BEGIN);
                if pos == seek_to {
                    let mut seek_buf = vec![0u8; 16];
                    if (api.stream_read)(rs, seek_buf.as_mut_ptr(), 16) != 16 {
                        return Err("short seek-read".to_string());
                    }
                    for k in 0..16i64 {
                        if seek_buf[k as usize] != ((seek_to + k) % 256) as u8 {
                            return Err("seek byte mismatch".to_string());
                        }
                    }
                    println!("    wrote+verified {total} bytes; seek to {seek_to} OK");
                } else {
                    println!("    wrote+verified {total} bytes; seek not supported by this backend (skipped)");
                }
                let rs2 = (api.open_stream_with_flags)(handle, fp.as_bytes().as_ptr(), fp.len() as i32, OPEN_READONLY);
                if rs2.is_null() {
                    return Err(format!("open_stream_with_flags: {}", api.last_error()));
                }
                let len_ok = (api.stream_get_length)(rs2) == total;
                (api.close_stream)(rs2);
                if !len_ok {
                    return Err("flags-open length mismatch".to_string());
                }
                Ok(())
            })();
            (api.close_stream)(rs);
            result?;

            // stream_set_length: truncation of encrypted streams is backend-dependent; best-effort.
            let tp = "/trunc.bin";
            let ts = (api.open_stream_with_flags)(handle, tp.as_bytes().as_ptr(), tp.len() as i32, OPEN_WRITEONLY | OPEN_CREATE | OPEN_TRUNCATE);
            if !ts.is_null() {
                (api.stream_write)(ts, chunk.as_ptr(), CHUNK as i32);
                (api.stream_set_length)(ts, 4096);
                (api.close_stream)(ts);
            }
        }
        Ok(())
    });

    unsafe {
        (api.close_vault)(handle);
    }

    // 4. Persistence: reopen with the passphrase and re-read.
    section(st, "Persistence", format, || {
        let h2 = api.open_vault(format, vault_dir, password, None, None);
        if h2.is_null() {
            return Err(format!("reopen failed: {}", api.last_error()));
        }
        let result = api.read_file_full(h2, pp).and_then(|got| {
            if got != persist {
                Err("persisted content mismatch".to_string())
            } else {
                Ok(())
            }
        });
        unsafe {
            (api.close_vault)(h2);
        }
        result
    });

    // 5/6. UVF-only: key rotation, public-key multi-user, password multi-user.
    if format == "uvf" {
        let rc = unsafe { (api.rotate_keys)(vb.as_ptr(), vb.len() as i32, pb.as_ptr(), pb.len() as i32, TITAN_VAULT_FORMAT_UVF) };
        if rc == TITAN_VAULT_SUCCESS {
            println!("  Key rotation tests for UVF: PASSED");
        } else {
            let e = api.last_error();
            if e.to_lowercase().contains("not implemented") {
                println!("  Key rotation tests for UVF: SKIPPED (not implemented)");
            } else {
                st.failed += 1;
                println!("  Key rotation tests for UVF: FAILED — {e}");
            }
        }

        section(st, "Public-key multi-user", format, || {
            let bob = "bob";
            let key_pw = "bob-key-pass-123";

            // 1. Generate bob's key pair (public key + password-encrypted private key) via the C ABI.
            let mut pub_buf = vec![0u8; 4096];
            let mut priv_buf = vec![0u8; 8192];
            let mut pub_size: i32 = pub_buf.len() as i32;
            let mut priv_size: i32 = priv_buf.len() as i32;
            unsafe {
                api.check(
                    (api.generate_user_keypair)(
                        key_pw.as_bytes().as_ptr(),
                        key_pw.len() as i32,
                        pub_buf.as_mut_ptr(),
                        &mut pub_size,
                        priv_buf.as_mut_ptr(),
                        &mut priv_size,
                    ),
                    "generate_user_keypair",
                )?;
            }
            pub_buf.truncate(pub_size as usize);
            priv_buf.truncate(priv_size as usize);
            println!("    generated bob key pair (public {pub_size}B, encrypted private {priv_size}B)");

            // 2. Grant bob access by PUBLIC key (admin needs no password from bob).
            unsafe {
                api.check(
                    (api.add_user_by_public_key)(
                        vb.as_ptr(),
                        vb.len() as i32,
                        pb.as_ptr(),
                        pb.len() as i32,
                        bob.as_bytes().as_ptr(),
                        bob.len() as i32,
                        pub_buf.as_ptr(),
                        pub_buf.len() as i32,
                    ),
                    "add_user_by_public_key",
                )?;
            }

            // 3. Open the vault as bob with his PRIVATE key and read the admin-written file.
            let read_as_bob = || -> Result<(), String> {
                let h = unsafe {
                    (api.load_uvf_with_key)(
                        vb.as_ptr(),
                        vb.len() as i32,
                        priv_buf.as_ptr(),
                        priv_buf.len() as i32,
                        key_pw.as_bytes().as_ptr(),
                        key_pw.len() as i32,
                        bob.as_bytes().as_ptr(),
                        bob.len() as i32,
                    )
                };
                if h.is_null() {
                    return Err(format!("load as bob failed: {}", api.last_error()));
                }
                let result = api.read_file_full(h, pp).and_then(|got| {
                    if got != persist {
                        Err("bob read mismatch".to_string())
                    } else {
                        Ok(())
                    }
                });
                unsafe {
                    (api.close_vault)(h);
                }
                result
            };
            read_as_bob()?;
            println!("    opened as bob (public-key user) and read the admin file OK");

            // 4. Rotate the key for public-key members — admin alone — then bob still reads.
            unsafe {
                api.check(
                    (api.rotate_keys_pubkey)(vb.as_ptr(), vb.len() as i32, pb.as_ptr(), pb.len() as i32),
                    "rotate_keys_pubkey",
                )?;
            }
            read_as_bob()?;
            println!("    rotated keys (no member password) and bob still reads OK");
            Ok(())
        });

        section(st, "Multi-user", format, || {
            let alice = "alice";
            let alice_pw = "alice-passphrase-123";
            unsafe {
                api.check(
                    (api.add_user)(
                        vb.as_ptr(),
                        vb.len() as i32,
                        pb.as_ptr(),
                        pb.len() as i32,
                        alice.as_bytes().as_ptr(),
                        alice.len() as i32,
                        alice_pw.as_bytes().as_ptr(),
                        alice_pw.len() as i32,
                    ),
                    "add_user",
                )?;
            }
            let mut ub: [*mut c_char; MAX_LIST] = [std::ptr::null_mut(); MAX_LIST];
            let mut um: i32 = MAX_LIST as i32;
            let n = unsafe {
                (api.get_vault_users)(vb.as_ptr(), vb.len() as i32, pb.as_ptr(), pb.len() as i32, ub.as_mut_ptr(), &mut um)
            };
            if n < 0 {
                return Err(format!("get_vault_users rc={n}: {}", api.last_error()));
            }
            let users = api.read_string_array(&ub, n as usize);
            println!("    vault users: {}", json_list(&users));
            if !users.iter().any(|u| u == alice) {
                return Err(format!("added user not listed (got {})", json_list(&users)));
            }

            // Best-effort: open as the new user (a known library limitation — reported, not failed).
            let alice_open = (|| -> Result<(), String> {
                let ah = api.open_vault("uvf", vault_dir, password, Some(alice), Some(alice_pw));
                if ah.is_null() {
                    return Err(api.last_error());
                }
                let result = api.read_file_full(ah, pp).and_then(|got| {
                    if got != persist {
                        Err("alice read mismatch".to_string())
                    } else {
                        Ok(())
                    }
                });
                unsafe {
                    (api.close_vault)(ah);
                }
                result
            })();
            match alice_open {
                Ok(()) => println!("    opened as second user and read the admin-written file OK"),
                Err(e) => println!("    ⚠ opening as a secondary user is not yet supported by the library: {e}"),
            }

            // Change a member's password (admin-driven), then remove the member and confirm they're gone.
            let alice_new_pw = "alice-passphrase-456";
            unsafe {
                api.check(
                    (api.change_uvf_user_password)(
                        vb.as_ptr(),
                        vb.len() as i32,
                        pb.as_ptr(),
                        pb.len() as i32,
                        alice.as_bytes().as_ptr(),
                        alice.len() as i32,
                        alice_new_pw.as_bytes().as_ptr(),
                        alice_new_pw.len() as i32,
                    ),
                    "change_uvf_user_password",
                )?;
                api.check(
                    (api.remove_user)(vb.as_ptr(), vb.len() as i32, pb.as_ptr(), pb.len() as i32, alice.as_bytes().as_ptr(), alice.len() as i32),
                    "remove_user",
                )?;
            }
            let mut ub2: [*mut c_char; MAX_LIST] = [std::ptr::null_mut(); MAX_LIST];
            let mut um2: i32 = MAX_LIST as i32;
            let n2 = unsafe {
                (api.get_vault_users)(vb.as_ptr(), vb.len() as i32, pb.as_ptr(), pb.len() as i32, ub2.as_mut_ptr(), &mut um2)
            };
            let users2 = api.read_string_array(&ub2, if n2 < 0 { 0 } else { n2 as usize });
            if users2.iter().any(|u| u == alice) {
                return Err(format!("removed user still listed (got {})", json_list(&users2)));
            }
            println!("    changed alice's password, then removed alice; users now: {}", json_list(&users2));
            Ok(())
        });
    }

    // 7. Maintenance (both formats): backup, secure-wipe, password change + reopen.
    section(st, "Maintenance", format, || {
        let backup_dir = std::env::temp_dir().join(format!("uvf-backup-{format}-{}", pid()));
        let _ = fs::remove_dir_all(&backup_dir);
        let bd = backup_dir.to_string_lossy().to_string();
        unsafe {
            api.check(
                (api.backup_files)(vb.as_ptr(), vb.len() as i32, bd.as_bytes().as_ptr(), bd.len() as i32, 1),
                "backup_files",
            )?;
        }
        if !backup_dir.exists() || walk(&backup_dir).is_empty() {
            return Err("backup produced no files".to_string());
        }

        let mut secret = b"super-secret-key-material".to_vec();
        unsafe {
            (api.secure_zero_memory)(secret.as_mut_ptr(), secret.len() as i32);
        }
        if secret.iter().any(|&b| b != 0) {
            return Err("secure_zero_memory did not zero the buffer".to_string());
        }

        let new_pw = format!("{password}-rotated");
        let np = new_pw.as_bytes();
        unsafe {
            if format == "uvf" {
                api.check(
                    (api.change_uvf_admin_password)(vb.as_ptr(), vb.len() as i32, pb.as_ptr(), pb.len() as i32, np.as_ptr(), np.len() as i32),
                    "change_uvf_admin_password",
                )?;
            } else {
                api.check(
                    (api.change_cryptomator_password)(vb.as_ptr(), vb.len() as i32, pb.as_ptr(), pb.len() as i32, np.as_ptr(), np.len() as i32),
                    "change_cryptomator_password",
                )?;
            }
        }
        let h3 = api.open_vault(format, vault_dir, &new_pw, None, None);
        if h3.is_null() {
            return Err(format!("reopen after password change failed: {}", api.last_error()));
        }
        let result = api.read_file_full(h3, pp).and_then(|got| {
            if got != persist {
                Err("content mismatch after password change".to_string())
            } else {
                Ok(())
            }
        });
        unsafe {
            (api.close_vault)(h3);
        }
        result?;
        let _ = fs::remove_dir_all(&backup_dir);
        println!("    backed up key files, secure-zeroed a buffer, changed the {format} password and re-read OK");
        Ok(())
    });

    println!("✅ {format} demo finished.");
    Ok(())
}

// ----- interop: unlock a REAL Cryptomator vault and byte-compare the files -----
fn find_interop_base() -> Option<PathBuf> {
    let mut starts: Vec<PathBuf> = Vec::new();
    if let Ok(cwd) = std::env::current_dir() {
        starts.push(cwd);
    }
    if let Ok(exe) = std::env::current_exe() {
        if let Some(dir) = exe.parent() {
            starts.push(dir.to_path_buf());
        }
    }
    for start in starts {
        let mut d: Option<&Path> = Some(start.as_path());
        while let Some(dir) = d {
            for cand in [
                dir.join("_test-cryptomator-vault"),
                dir.join("Demo").join("_test-cryptomator-vault"),
            ] {
                if cand.join("smartinventure").join("masterkey.cryptomator").exists() {
                    return Some(cand);
                }
            }
            d = dir.parent();
        }
    }
    None
}

fn run_interop(api: &Api) -> bool {
    println!("\n========== Cryptomator interop (real vault) ==========");
    let base = match find_interop_base() {
        Some(b) => b,
        None => {
            println!("(Cryptomator interop skipped — Demo/_test-cryptomator-vault not found)");
            return true;
        }
    };
    let vault_dir = base.join("smartinventure").to_string_lossy().to_string();
    let orig_dir = base.join("original-files");
    let password = "smartinventure"; // demo vault — hardcoded on purpose

    let vb = vault_dir.as_bytes();
    let pb = password.as_bytes();
    let handle = unsafe { (api.load_cryptomator)(vb.as_ptr(), vb.len() as i32, pb.as_ptr(), pb.len() as i32) };
    if handle.is_null() {
        println!("Unlock failed: {}", api.last_error());
        return false;
    }
    let mut all_ok = true;
    let result = (|| -> Result<bool, String> {
        println!("Unlocked real Cryptomator vault at {vault_dir}");
        for d in ["/", "/mysubfolder1", "/mysubfolder1/mysubfolder2"] {
            println!("  {d}  ->  {}", json_list(&api.list_dir(handle, d)?));
        }
        let cases = [
            ("/Perfect-albums.txt", "Perfect-albums.txt"),
            ("/mysubfolder1/banana.jpg", "banana.jpg"),
            ("/mysubfolder1/mysubfolder2/Rubicon - Rivers - lyrics.txt", "Rubicon - Rivers - lyrics.txt"),
        ];
        let mut ok_all = true;
        for (vault_path, orig_name) in cases {
            let decrypted = api.read_file_full(handle, vault_path)?;
            let orig = fs::read(orig_dir.join(orig_name)).map_err(|e| e.to_string())?;
            let ok = decrypted == orig;
            if !ok {
                ok_all = false;
            }
            println!(
                "  {} {vault_path}  ({} B)  bytes {}",
                if ok { "✓" } else { "✗" },
                decrypted.len(),
                if ok { "match" } else { "MISMATCH" }
            );
        }
        Ok(ok_all)
    })();
    match result {
        Ok(ok) => {
            all_ok = ok;
            if all_ok {
                println!("✅ Reading a real Cryptomator vault worked — all files decrypted and byte-matched the originals.");
            } else {
                println!("❌ Cryptomator interop FAILED — byte mismatch.");
            }
        }
        Err(e) => {
            println!("❌ Cryptomator interop FAILED: {e}");
            all_ok = false;
        }
    }
    unsafe {
        (api.close_vault)(handle);
    }
    all_ok
}

// ----- benchmark -----
fn bench_one(api: &Api, format: &str, size_bytes: i64, chunk_size: usize) -> Result<(), String> {
    println!("\n----- {} -----", upper(format));
    let dir = std::env::temp_dir().join(format!("uvf-bench-{format}-{}", pid()));
    let _ = fs::remove_dir_all(&dir);
    let vault_path = dir.join("vault");
    fs::create_dir_all(&vault_path).map_err(|e| e.to_string())?;
    let plain = dir.join("plain.bin");
    let password = "bench-pass-123";
    let vault_dir = vault_path.to_string_lossy().to_string();
    let vb = vault_dir.as_bytes();
    let pb = password.as_bytes();

    let report = |label: &str, ms: f64| {
        println!("  {label:<38} {ms:>7.0} ms   {:>8.1} MB/s", mbps(size_bytes as f64, ms));
    };

    let mut chunk = vec![0u8; chunk_size];
    for (i, b) in chunk.iter_mut().enumerate() {
        *b = (i & 0xff) as u8; // non-trivial data (avoid sparse-file effects)
    }

    let run = (|| -> Result<(), String> {
        // (a) create the plaintext file on disk — gauges raw medium write speed
        let mut t = Instant::now();
        {
            let mut f = fs::File::create(&plain).map_err(|e| e.to_string())?;
            let mut w: i64 = 0;
            while w < size_bytes {
                let n = std::cmp::min(chunk_size as i64, size_bytes - w) as usize;
                f.write_all(&chunk[..n]).map_err(|e| e.to_string())?;
                w += n as i64;
            }
            f.flush().map_err(|e| e.to_string())?;
            f.sync_all().map_err(|e| e.to_string())?;
        }
        report("create file (disk write, may be cached)", elapsed_ms(t));

        unsafe {
            if format == "uvf" {
                api.check((api.create_uvf)(vb.as_ptr(), vb.len() as i32, pb.as_ptr(), pb.len() as i32, 1, 0, 0), "create_uvf_vault")?;
            } else {
                api.check((api.create_cryptomator)(vb.as_ptr(), vb.len() as i32, pb.as_ptr(), pb.len() as i32), "create_cryptomator_vault")?;
            }
        }
        let handle = if format == "uvf" {
            unsafe { (api.load_uvf)(vb.as_ptr(), vb.len() as i32, pb.as_ptr(), pb.len() as i32, std::ptr::null(), 0) }
        } else {
            unsafe { (api.load_cryptomator)(vb.as_ptr(), vb.len() as i32, pb.as_ptr(), pb.len() as i32) }
        };
        if handle.is_null() {
            return Err(format!("load failed: {}", api.last_error()));
        }

        let inner = (|| -> Result<(), String> {
            let fp = "/big.bin";
            // (b) encrypt — stream the plaintext into the vault
            t = Instant::now();
            unsafe {
                let ws = (api.open_write_stream)(handle, fp.as_bytes().as_ptr(), fp.len() as i32);
                if ws.is_null() {
                    return Err(format!("open_write_stream: {}", api.last_error()));
                }
                let mut f = fs::File::open(&plain).map_err(|e| e.to_string())?;
                let mut rbuf = vec![0u8; chunk_size];
                loop {
                    let rd = f.read(&mut rbuf).map_err(|e| e.to_string())?;
                    if rd == 0 {
                        break;
                    }
                    if (api.stream_write)(ws, rbuf.as_ptr(), rd as i32) != rd as i32 {
                        (api.close_stream)(ws);
                        return Err("short write".to_string());
                    }
                }
                (api.close_stream)(ws);
            }
            report(&format!("encrypt ({format})"), elapsed_ms(t));

            // (c) decrypt — stream it back out of the vault (discarding the plaintext)
            t = Instant::now();
            unsafe {
                let rs = (api.open_read_stream)(handle, fp.as_bytes().as_ptr(), fp.len() as i32);
                if rs.is_null() {
                    return Err(format!("open_read_stream: {}", api.last_error()));
                }
                let mut dbuf = vec![0u8; chunk_size];
                let mut total: i64 = 0;
                loop {
                    let got = (api.stream_read)(rs, dbuf.as_mut_ptr(), chunk_size as i32);
                    if got <= 0 {
                        break;
                    }
                    total += got as i64;
                }
                (api.close_stream)(rs);
                if total != size_bytes {
                    return Err(format!("decrypt size {total} != {size_bytes}"));
                }
            }
            report(&format!("decrypt ({format})"), elapsed_ms(t));

            // (d) read the plaintext file back from disk — gauges raw medium read speed
            t = Instant::now();
            {
                let mut f = fs::File::open(&plain).map_err(|e| e.to_string())?;
                let mut rbuf = vec![0u8; chunk_size];
                while f.read(&mut rbuf).map_err(|e| e.to_string())? > 0 { /* discard */ }
            }
            report("read file (disk read, may be cached)", elapsed_ms(t));
            Ok(())
        })();
        unsafe {
            (api.close_vault)(handle);
        }
        inner
    })();

    let _ = fs::remove_dir_all(&dir);
    run
}

fn run_benchmark(api: &Api, size_gb: f64) -> Result<(), String> {
    let size_bytes = (size_gb * 1024.0 * 1024.0 * 1024.0 + 0.5) as i64;
    const CHUNK: usize = 4 * 1024 * 1024; // 4 MiB
    println!("\n========== Benchmark ({size_gb} GB per format, {} MiB chunks) ==========", CHUNK >> 20);
    println!("  (disk read/write rows may just reflect the OS cache — pass --size larger than your RAM for disk-bound numbers)");
    for format in ["uvf", "cryptomator"] {
        bench_one(api, format, size_bytes, CHUNK)?;
    }
    Ok(())
}

// ----- library discovery -----
fn lib_file_name() -> &'static str {
    if cfg!(target_os = "windows") {
        "TitanVault.dll"
    } else if cfg!(target_os = "macos") {
        "libTitanVault.dylib"
    } else {
        "libTitanVault.so"
    }
}

fn rid() -> String {
    let os = if cfg!(target_os = "windows") {
        "win-"
    } else if cfg!(target_os = "macos") {
        "osx-"
    } else {
        "linux-"
    };
    let arch = if cfg!(target_arch = "aarch64") { "arm64" } else { "x64" };
    format!("{os}{arch}")
}

fn discover_lib() -> PathBuf {
    let file = lib_file_name();
    let rid = rid();
    let exe_dir = std::env::current_exe().ok().and_then(|p| p.parent().map(|d| d.to_path_buf()));
    let cwd = std::env::current_dir().ok();

    let mut candidates: Vec<PathBuf> = Vec::new();
    // 1. next to the executable   2. the current working directory
    if let Some(ed) = &exe_dir {
        candidates.push(ed.join(file));
    }
    if let Some(c) = &cwd {
        candidates.push(c.join(file));
    }
    // 3. walk up from cwd and the exe dir for Dist/Native/<rid>/<file>
    for start in [cwd.clone(), exe_dir.clone()].into_iter().flatten() {
        let mut d: Option<&Path> = Some(start.as_path());
        while let Some(dir) = d {
            candidates.push(dir.join("Dist").join("Native").join(&rid).join(file));
            d = dir.parent();
        }
    }
    for c in &candidates {
        if c.exists() {
            return c.clone();
        }
    }
    // Fall back to the Dist path so the "not found" message points at the build.
    exe_dir
        .map(|ed| ed.join("Dist").join("Native").join(&rid).join(file))
        .unwrap_or_else(|| PathBuf::from(file))
}

// ----- args -----
struct Args {
    lib: PathBuf,
    format: Option<String>,
    vault: PathBuf,
    password: String,
    benchmark: bool,
    interop: bool,
    size_gb: f64,
}

fn parse_args() -> Args {
    // Precedence for the library: --lib > TITANVAULT_LIB env > discovery.
    let lib_default = match std::env::var("TITANVAULT_LIB") {
        Ok(v) if !v.is_empty() => PathBuf::from(v),
        _ => discover_lib(),
    };
    let mut a = Args {
        lib: lib_default,
        format: None, // default: run BOTH formats
        vault: std::env::temp_dir().join("uvf-rust-demo"),
        password: "correct horse battery staple".to_string(),
        benchmark: false,
        interop: false,
        size_gb: 1.0,
    };
    let argv: Vec<String> = std::env::args().skip(1).collect();
    let mut i = 0;
    while i < argv.len() {
        let next = |i: &mut usize| -> String {
            *i += 1;
            argv.get(*i).cloned().unwrap_or_default()
        };
        match argv[i].as_str() {
            "--lib" => a.lib = PathBuf::from(next(&mut i)),
            "--format" => a.format = Some(next(&mut i)),
            "--vault" => a.vault = PathBuf::from(next(&mut i)),
            "--password" => a.password = next(&mut i),
            "--benchmark" | "--bench" => a.benchmark = true,
            "--size" => a.size_gb = next(&mut i).parse().unwrap_or(1.0),
            "--cryptomator-interop" | "--interop" => a.interop = true,
            _ => {}
        }
        i += 1;
    }
    a
}

fn main() {
    let args = parse_args();
    if !args.lib.exists() {
        eprintln!(
            "Native library not found: {}\n\
             Build it first:  ../../BuildScripts/build.ps1 -Task aot   (or build.sh --task aot)\n\
             Then it loads automatically (same folder / cwd / ../../Dist/Native/<rid>/), or pass --lib <path> / set TITANVAULT_LIB.\n\
             Note: the library must match this binary's architecture ({}).",
            args.lib.display(),
            rid()
        );
        std::process::exit(1);
    }
    let api = match Api::load(&args.lib) {
        Ok(api) => api,
        Err(e) => {
            eprintln!("{e} (architecture mismatch?)");
            std::process::exit(1);
        }
    };
    println!("TitanVault version: {}", api.version());

    // Focused modes (run only the requested thing).
    if args.interop {
        std::process::exit(if run_interop(&api) { 0 } else { 1 });
    }
    if args.benchmark {
        if let Err(e) = run_benchmark(&api, args.size_gb) {
            eprintln!("\n❌ benchmark aborted: {e}");
            std::process::exit(1);
        }
        std::process::exit(0);
    }

    let mut st = State { failed: 0 };

    // Functional sections, for one format (--format) or both (default).
    let formats: Vec<String> = match &args.format {
        Some(f) => vec![f.clone()],
        None => vec!["uvf".to_string(), "cryptomator".to_string()],
    };
    for format in &formats {
        let vault_dir = args.vault.join(format).to_string_lossy().to_string();
        if let Err(e) = run_demo(&api, format, &vault_dir, &args.password, &mut st) {
            st.failed += 1;
            println!("\n❌ {format} demo aborted: {e}");
        }
    }

    // A full run (no --format) also exercises the real-Cryptomator interop and a quick benchmark.
    if args.format.is_none() {
        if !run_interop(&api) {
            st.failed += 1;
        }
        if let Err(e) = run_benchmark(&api, 0.25) {
            st.failed += 1;
            println!("\n❌ benchmark aborted: {e}");
        }
    }

    if st.failed == 0 {
        println!("\n✅ All Rust demo sections passed.");
        std::process::exit(0);
    } else {
        println!("\n❌ {} section(s) failed.", st.failed);
        std::process::exit(1);
    }
}
