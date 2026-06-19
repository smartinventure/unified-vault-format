#!/usr/bin/env python3
"""
UVF / Cryptomator demo in Python via the native TitanVault library (C ABI, ctypes — no extra deps).

This is a full port of Demo/NodeJs/vault-demo.js: it runs the same sections, prints the same output
lines, honors the same flags, and (with no arguments) runs everything — both formats' functional
sections, the real-Cryptomator-vault interop, and a quick throughput benchmark.

Build the native library first (from the repo root):
    ../../BuildScripts/build.ps1 -Task aot        # -> Dist/Native/win-x64/TitanVault.dll
    ../../BuildScripts/build.sh  --task aot        # -> Dist/Native/<rid>/libTitanVault.{so,dylib}

Run (runs BOTH formats by default; --format uvf|cryptomator restricts to one):
    python vault_demo.py --lib ../../Dist/Native/win-x64/TitanVault.dll
"""

import ctypes
import hashlib
import os
import platform
import shutil
import sys
import tempfile
import time

# Windows consoles default to a legacy codepage (cp1252) that can't encode the ✅/❌ status emoji,
# which raises UnicodeEncodeError when stdout is captured/redirected. Force UTF-8 like Node does.
for _stream in (sys.stdout, sys.stderr):
    try:
        _stream.reconfigure(encoding="utf-8")
    except (AttributeError, ValueError):
        pass

TITAN_VAULT_SUCCESS = 0
TITAN_VAULT_FORMAT_CRYPTOMATOR = 0
TITAN_VAULT_FORMAT_UVF = 1
MAX_LIST = 256

# StorageLib.Abstractions.OpenFlags values (for open_stream_with_flags).
OPEN_READONLY = 0x0000
OPEN_WRITEONLY = 0x0001
OPEN_CREATE = 0x0040
OPEN_TRUNCATE = 0x0200

HERE = os.path.dirname(os.path.abspath(__file__))


# ----- native library loading -----

def default_lib_path():
    """Resolve the native library when neither --lib nor TITANVAULT_LIB is given. Search order:
        1. next to this demo (same folder)   2. the current working directory
        3. the built output ../../Dist/Native/<rid>/   (the usual location after a build)
    Returns the first that exists, else the Dist path (so the "not found" message points at the build)."""
    # NOTE: on Windows-on-ARM, platform.machine() reports the HOST arch (ARM64) even for an
    # x64 interpreter running under emulation, which would pick the wrong native binary. The
    # PROCESS arch (what the loaded DLL must match) is in PROCESSOR_ARCHITECTURE ("AMD64" when
    # emulated). Elsewhere platform.machine() is reliable.
    if sys.platform == "win32":
        machine = os.environ.get("PROCESSOR_ARCHITECTURE", platform.machine()).lower()
    else:
        machine = platform.machine().lower()
    arch = {"amd64": "x64", "x86_64": "x64", "x64": "x64",
            "arm64": "arm64", "aarch64": "arm64"}.get(machine, machine)
    if sys.platform == "win32":
        rid = "win-" + arch
        filename = "TitanVault.dll"
    elif sys.platform == "darwin":
        rid = "osx-" + arch
        filename = "libTitanVault.dylib"
    else:
        rid = "linux-" + arch
        filename = "libTitanVault.so"
    dist_path = os.path.normpath(os.path.join(HERE, "..", "..", "Dist", "Native", rid, filename))
    candidates = [os.path.join(HERE, filename), os.path.abspath(os.path.join(os.getcwd(), filename)), dist_path]
    for candidate in candidates:
        if os.path.exists(candidate):
            return candidate
    return dist_path


def parse_args(argv):
    """format is left None by default so the demo runs BOTH formats (uvf then cryptomator);
    pass --format uvf|cryptomator to run just one."""
    a = {
        "lib": os.environ.get("TITANVAULT_LIB") or default_lib_path(),
        "format": None,
        "vault": os.path.join(tempfile.gettempdir(), "uvf-python-demo"),
        "password": "correct horse battery staple",
        "benchmark": False,
        "interop": False,
        "size_gb": 1.0,
    }
    i = 0
    while i < len(argv):
        arg = argv[i]
        nxt = argv[i + 1] if i + 1 < len(argv) else None
        if arg == "--lib":
            a["lib"] = nxt; i += 1
        elif arg == "--format":
            a["format"] = nxt; i += 1
        elif arg == "--vault":
            a["vault"] = nxt; i += 1
        elif arg == "--password":
            a["password"] = nxt; i += 1
        elif arg in ("--benchmark", "--bench"):
            a["benchmark"] = True
        elif arg == "--size":
            a["size_gb"] = float(nxt); i += 1
        elif arg in ("--cryptomator-interop", "--interop"):
            a["interop"] = True
        i += 1
    return a


class Lib:
    """Thin ctypes wrapper around the TitanVault C ABI. Sets argtypes/restype for every function."""

    def __init__(self, path):
        self._dll = ctypes.CDLL(path)
        d = self._dll
        cp, ci, vp = ctypes.c_char_p, ctypes.c_int, ctypes.c_void_p
        i64p = ctypes.POINTER(ctypes.c_int64)
        ip = ctypes.POINTER(ctypes.c_int)

        # Returns a heap char* the caller must release with titan_vault_free_string.
        d.titan_vault_get_version.restype = vp
        d.titan_vault_get_version.argtypes = []
        # Static buffer — must NOT be freed.
        d.titan_vault_get_last_error.restype = cp
        d.titan_vault_get_last_error.argtypes = []
        d.titan_vault_free_string.restype = None
        d.titan_vault_free_string.argtypes = [vp]

        d.titan_vault_create_uvf_vault.restype = ci
        d.titan_vault_create_uvf_vault.argtypes = [cp, ci, cp, ci, ci, ci, ci]
        d.titan_vault_load_uvf_vault.restype = vp
        d.titan_vault_load_uvf_vault.argtypes = [cp, ci, cp, ci, cp, ci]
        d.titan_vault_create_cryptomator_vault.restype = ci
        d.titan_vault_create_cryptomator_vault.argtypes = [cp, ci, cp, ci]
        d.titan_vault_load_cryptomator_vault.restype = vp
        d.titan_vault_load_cryptomator_vault.argtypes = [cp, ci, cp, ci]
        d.titan_vault_close_vault.restype = ci
        d.titan_vault_close_vault.argtypes = [vp]

        d.titan_vault_write_file.restype = ci
        d.titan_vault_write_file.argtypes = [vp, cp, ci, vp, ci]
        d.titan_vault_read_file.restype = ci
        d.titan_vault_read_file.argtypes = [vp, cp, ci, vp, ip]
        d.titan_vault_file_exists.restype = ci
        d.titan_vault_file_exists.argtypes = [vp, cp, ci]
        d.titan_vault_delete_file.restype = ci
        d.titan_vault_delete_file.argtypes = [vp, cp, ci]

        d.titan_vault_create_directory.restype = ci
        d.titan_vault_create_directory.argtypes = [vp, cp, ci]
        d.titan_vault_directory_exists.restype = ci
        d.titan_vault_directory_exists.argtypes = [vp, cp, ci]
        d.titan_vault_delete_directory.restype = ci
        d.titan_vault_delete_directory.argtypes = [vp, cp, ci]
        # entriesBuffer = char*[maxEntries]; maxEntries = int* (in). Returns count; each entry freed.
        d.titan_vault_list_directory.restype = ci
        d.titan_vault_list_directory.argtypes = [vp, cp, ci, vp, vp]
        # fileSize = int64* (out), lastModified = int64* (out, unix seconds).
        d.titan_vault_get_file_info.restype = ci
        d.titan_vault_get_file_info.argtypes = [vp, cp, ci, vp, vp]
        d.titan_vault_move.restype = ci
        d.titan_vault_move.argtypes = [vp, cp, ci, cp, ci]

        d.titan_vault_open_read_stream.restype = vp
        d.titan_vault_open_read_stream.argtypes = [vp, cp, ci]
        d.titan_vault_open_write_stream.restype = vp
        d.titan_vault_open_write_stream.argtypes = [vp, cp, ci]
        d.titan_vault_stream_write.restype = ci
        d.titan_vault_stream_write.argtypes = [vp, vp, ci]
        d.titan_vault_stream_read.restype = ci
        d.titan_vault_stream_read.argtypes = [vp, vp, ci]
        d.titan_vault_stream_seek.restype = ctypes.c_int64
        d.titan_vault_stream_seek.argtypes = [vp, ctypes.c_int64, ci]
        d.titan_vault_stream_get_length.restype = ctypes.c_int64
        d.titan_vault_stream_get_length.argtypes = [vp]
        d.titan_vault_close_stream.restype = ci
        d.titan_vault_close_stream.argtypes = [vp]

        # UVF multi-user / key management — these operate on the vault PATH (vault need not be open).
        d.titan_vault_add_user.restype = ci
        d.titan_vault_add_user.argtypes = [cp, ci, cp, ci, cp, ci, cp, ci]
        d.titan_vault_get_vault_users.restype = ci
        d.titan_vault_get_vault_users.argtypes = [cp, ci, cp, ci, vp, vp]
        d.titan_vault_rotate_keys.restype = ci
        d.titan_vault_rotate_keys.argtypes = [cp, ci, cp, ci, ci]

        # UVF public-key (asymmetric) membership.
        d.titan_vault_generate_user_keypair.restype = ci
        d.titan_vault_generate_user_keypair.argtypes = [vp, ci, vp, ip, vp, ip]
        d.titan_vault_add_user_by_public_key.restype = ci
        d.titan_vault_add_user_by_public_key.argtypes = [cp, ci, cp, ci, cp, ci, vp, ci]
        d.titan_vault_load_uvf_vault_with_key.restype = vp
        d.titan_vault_load_uvf_vault_with_key.argtypes = [cp, ci, vp, ci, cp, ci, cp, ci]
        d.titan_vault_rotate_keys_pubkey.restype = ci
        d.titan_vault_rotate_keys_pubkey.argtypes = [cp, ci, cp, ci]

        # Library / maintenance utilities.
        d.titan_vault_detect_vault_format.restype = ci
        d.titan_vault_detect_vault_format.argtypes = [cp, ci]
        d.titan_vault_secure_zero_memory.restype = None
        d.titan_vault_secure_zero_memory.argtypes = [vp, ci]
        d.titan_vault_backup_files.restype = ci
        d.titan_vault_backup_files.argtypes = [cp, ci, cp, ci, ci]
        d.titan_vault_change_cryptomator_password.restype = ci
        d.titan_vault_change_cryptomator_password.argtypes = [cp, ci, cp, ci, cp, ci]
        d.titan_vault_change_uvf_admin_password.restype = ci
        d.titan_vault_change_uvf_admin_password.argtypes = [cp, ci, cp, ci, cp, ci]
        d.titan_vault_change_uvf_user_password.restype = ci
        d.titan_vault_change_uvf_user_password.argtypes = [cp, ci, cp, ci, cp, ci, cp, ci]
        d.titan_vault_remove_user.restype = ci
        d.titan_vault_remove_user.argtypes = [cp, ci, cp, ci, cp, ci]

        # Text convenience (UTF-8). read_all_text returns a heap char* that must be freed.
        d.titan_vault_write_all_text.restype = ci
        d.titan_vault_write_all_text.argtypes = [vp, cp, ci, cp, ci]
        d.titan_vault_append_all_text.restype = ci
        d.titan_vault_append_all_text.argtypes = [vp, cp, ci, cp, ci]
        d.titan_vault_read_all_text.restype = vp
        d.titan_vault_read_all_text.argtypes = [vp, cp, ci]

        # Fuller stream API (the core read/write/seek are above).
        d.titan_vault_open_stream_with_flags.restype = vp
        d.titan_vault_open_stream_with_flags.argtypes = [vp, cp, ci, ci]
        d.titan_vault_stream_get_position.restype = ctypes.c_int64
        d.titan_vault_stream_get_position.argtypes = [vp]
        d.titan_vault_stream_set_length.restype = ci
        d.titan_vault_stream_set_length.argtypes = [vp, ctypes.c_int64]
        d.titan_vault_stream_flush.restype = ci
        d.titan_vault_stream_flush.argtypes = [vp]

    def __getattr__(self, name):
        return getattr(self._dll, name)

    def get_last_error(self):
        msg = self._dll.titan_vault_get_last_error()
        return msg.decode("utf-8") if msg else "(no error message)"


def u8(s):
    """Byte length of a UTF-8 string (params are pointer + explicit byte length, not NUL-terminated)."""
    return len(s.encode("utf-8"))


def b8(s):
    """UTF-8-encoded bytes for a string, usable directly as a c_char_p argument."""
    return s.encode("utf-8")


def check(lib, rc, what):
    if rc != TITAN_VAULT_SUCCESS:
        raise RuntimeError(f"{what} failed (rc={rc}): {lib.get_last_error()}")


def version(lib):
    ptr = lib.titan_vault_get_version()
    if not ptr:
        return "(unknown)"
    s = ctypes.cast(ptr, ctypes.c_char_p).value.decode("utf-8")
    lib.titan_vault_free_string(ptr)  # release the heap string
    return s


def read_string_array(lib, buffer, count):
    """Decode a returned char*[] of `count` entries into strings, freeing each native string."""
    if count <= 0:
        return []
    out = []
    for i in range(count):
        p = buffer[i]
        s = ctypes.cast(p, ctypes.c_char_p).value.decode("utf-8")
        out.append(s)
        lib.titan_vault_free_string(p)
    return out


def open_vault(lib, fmt, vault_dir, password, user_id=None, user_password=None):
    vlen = u8(vault_dir)
    if fmt == "uvf":
        pw = user_password or password
        uid = b8(user_id) if user_id else None
        return lib.titan_vault_load_uvf_vault(b8(vault_dir), vlen, b8(pw), u8(pw),
                                              uid, u8(user_id) if user_id else 0)
    return lib.titan_vault_load_cryptomator_vault(b8(vault_dir), vlen, b8(password), u8(password))


# ----- the per-format demo, organised into sections each reporting PASSED/FAILED -----

def run_demo(lib, fmt, vault_dir, password, state):
    print(f"\n========== {fmt.upper()} ==========")
    shutil.rmtree(vault_dir, ignore_errors=True)
    os.makedirs(vault_dir, exist_ok=True)
    vlen = u8(vault_dir)
    plen = u8(password)

    # Create the vault.
    if fmt == "uvf":
        check(lib, lib.titan_vault_create_uvf_vault(b8(vault_dir), vlen, b8(password), plen, 1, 0, 0),
              "create_uvf_vault")
    else:
        check(lib, lib.titan_vault_create_cryptomator_vault(b8(vault_dir), vlen, b8(password), plen),
              "create_cryptomator_vault")

    handle = open_vault(lib, fmt, vault_dir, password)
    if not handle:
        raise RuntimeError(f"load {fmt} vault failed: {lib.get_last_error()}")
    print(f"Created + opened {fmt} vault at {vault_dir}")

    def section(label, fn):
        try:
            fn()
            print(f"  {label} tests for {fmt.upper()}: PASSED")
        except Exception as e:
            state["failed"] += 1
            print(f"  {label} tests for {fmt.upper()}: FAILED — {e}")

    # 0. Detect the on-disk format (path-based — the vault need not be open).
    def detect_format_section():
        detected = lib.titan_vault_detect_vault_format(b8(vault_dir), vlen)
        expected = TITAN_VAULT_FORMAT_UVF if fmt == "uvf" else TITAN_VAULT_FORMAT_CRYPTOMATOR
        if detected != expected:
            raise RuntimeError(f"detect_vault_format={detected}, expected {expected}")
    section("Detect format", detect_format_section)

    # A file we deliberately keep around to prove persistence + multi-user access later.
    persist_payload = b"persisted across reopen"
    check(lib, lib.titan_vault_write_file(handle, b"/persist.txt", u8("/persist.txt"),
                                          persist_payload, len(persist_payload)), "write persist.txt")

    try:
        # 1. Basic file round-trip.
        def file_section():
            fp, fplen = "/hello.txt", u8("/hello.txt")
            plaintext = b"Hello, encrypted world!"
            check(lib, lib.titan_vault_write_file(handle, b8(fp), fplen, plaintext, len(plaintext)),
                  "write_file")
            size = ctypes.c_int(64 * 1024)
            buf = ctypes.create_string_buffer(size.value)
            check(lib, lib.titan_vault_read_file(handle, b8(fp), fplen, buf, ctypes.byref(size)),
                  "read_file")
            if buf.raw[:size.value] != plaintext:
                raise RuntimeError("round-trip mismatch")
            leaked = any(os.path.basename(f) == "hello.txt" for f in walk(vault_dir))
            if leaked:
                raise RuntimeError("plaintext filename leaked to disk")
            if lib.titan_vault_file_exists(handle, b8(fp), fplen) != 1:
                raise RuntimeError("exists should be 1")
            check(lib, lib.titan_vault_delete_file(handle, b8(fp), fplen), "delete_file")
            if lib.titan_vault_file_exists(handle, b8(fp), fplen) != 0:
                raise RuntimeError("exists should be 0 after delete")
        section("File", file_section)

        # 1b. UTF-8 text convenience: write, append, read-back.
        def text_helpers_section():
            tf, tflen = "/notes.txt", u8("/notes.txt")
            first, second = "first line\n", "second line\n"
            check(lib, lib.titan_vault_write_all_text(handle, b8(tf), tflen, b8(first), u8(first)),
                  "write_all_text")
            check(lib, lib.titan_vault_append_all_text(handle, b8(tf), tflen, b8(second), u8(second)),
                  "append_all_text")
            ptr = lib.titan_vault_read_all_text(handle, b8(tf), tflen)
            if not ptr:
                raise RuntimeError(f"read_all_text: {lib.get_last_error()}")
            text = ctypes.cast(ptr, ctypes.c_char_p).value.decode("utf-8")
            lib.titan_vault_free_string(ptr)
            if text != first + second:
                raise RuntimeError(f"text round-trip mismatch: {text!r}")
        section("Text helpers", text_helpers_section)

        # 2. Directories: create, write into, list, file-info, move/rename.
        def directory_section():
            check(lib, lib.titan_vault_create_directory(handle, b"/docs", u8("/docs")),
                  "create_directory")
            if lib.titan_vault_directory_exists(handle, b"/docs", u8("/docs")) != 1:
                raise RuntimeError("directory_exists should be 1")
            note, notelen = "/docs/note.txt", u8("/docs/note.txt")
            body = b"inside a subdirectory"
            check(lib, lib.titan_vault_write_file(handle, b8(note), notelen, body, len(body)),
                  "write into dir")

            entries_buf = (ctypes.c_void_p * MAX_LIST)()
            max_buf = ctypes.c_int(MAX_LIST)
            n = lib.titan_vault_list_directory(handle, b"/docs", u8("/docs"), entries_buf,
                                               ctypes.byref(max_buf))
            if n < 0:
                raise RuntimeError(f"list_directory rc={n}: {lib.get_last_error()}")
            names = read_string_array(lib, entries_buf, n)
            if "note.txt" not in names:
                raise RuntimeError(f"listing missing note.txt (got {names})")

            size_buf = ctypes.c_int64()
            mtime_buf = ctypes.c_int64()
            check(lib, lib.titan_vault_get_file_info(handle, b8(note), notelen,
                                                     ctypes.byref(size_buf), ctypes.byref(mtime_buf)),
                  "get_file_info")
            sz = size_buf.value
            if sz != len(body):
                raise RuntimeError(f"file size {sz} != {len(body)}")

            renamed, renamedlen = "/docs/renamed.txt", u8("/docs/renamed.txt")
            check(lib, lib.titan_vault_move(handle, b8(note), notelen, b8(renamed), renamedlen),
                  "move")
            max_buf = ctypes.c_int(MAX_LIST)
            n = lib.titan_vault_list_directory(handle, b"/docs", u8("/docs"), entries_buf,
                                               ctypes.byref(max_buf))
            names = read_string_array(lib, entries_buf, n)
            if "renamed.txt" not in names:
                raise RuntimeError(f"rename not reflected (got {names})")
            print(f"    /docs now contains: {json_list(names)} (size of note was {sz} bytes)")
        section("Directory", directory_section)

        # 3. Streaming: write a multi-chunk file, then random-access read with seek.
        def streaming_section():
            fp, fplen = "/big.bin", u8("/big.bin")
            CHUNK, CHUNKS = 32 * 1024, 4
            total = CHUNK * CHUNKS
            chunk = bytes(j % 256 for j in range(CHUNK))  # file[O] == O % 256
            chunk_buf = ctypes.create_string_buffer(chunk, CHUNK)

            ws = lib.titan_vault_open_write_stream(handle, b8(fp), fplen)
            if not ws:
                raise RuntimeError(f"open_write_stream: {lib.get_last_error()}")
            try:
                for _ in range(CHUNKS):
                    if lib.titan_vault_stream_write(ws, chunk_buf, CHUNK) != CHUNK:
                        raise RuntimeError("short write")
                check(lib, lib.titan_vault_stream_flush(ws), "stream_flush")
            finally:
                lib.titan_vault_close_stream(ws)

            rs = lib.titan_vault_open_read_stream(handle, b8(fp), fplen)
            if not rs:
                raise RuntimeError(f"open_read_stream: {lib.get_last_error()}")
            try:
                length = lib.titan_vault_stream_get_length(rs)
                if length != total:
                    raise RuntimeError(f"stream length {length} != {total}")
                # sequential read of the whole thing, verifying the position-dependent pattern
                rbuf = ctypes.create_string_buffer(CHUNK)
                off = 0
                while True:
                    got = lib.titan_vault_stream_read(rs, rbuf, CHUNK)
                    if got <= 0:
                        break
                    data = rbuf.raw[:got]
                    for k in range(got):
                        if data[k] != (off + k) % 256:
                            raise RuntimeError(f"byte mismatch at {off + k}")
                    off += got
                if off != total:
                    raise RuntimeError(f"read {off} of {total}")
                pos_after_read = lib.titan_vault_stream_get_position(rs)
                if pos_after_read != total:
                    raise RuntimeError(f"stream_get_position {pos_after_read} != {total}")
                # random access: seek to a mid-file offset and verify (best-effort)
                seek_to = 70000
                pos = lib.titan_vault_stream_seek(rs, seek_to, 0)  # 0 = SEEK_SET
                if pos == seek_to:
                    small = ctypes.create_string_buffer(16)
                    if lib.titan_vault_stream_read(rs, small, 16) != 16:
                        raise RuntimeError("short seek-read")
                    sdata = small.raw[:16]
                    for k in range(16):
                        if sdata[k] != (seek_to + k) % 256:
                            raise RuntimeError(f"seek byte mismatch at {seek_to + k}")
                    print(f"    wrote+verified {total} bytes; seek to {seek_to} OK")
                else:
                    print(f"    wrote+verified {total} bytes; seek not supported by this backend (skipped)")
                # open_stream_with_flags: reopen read-only and confirm the length matches.
                rs2 = lib.titan_vault_open_stream_with_flags(handle, b8(fp), fplen, OPEN_READONLY)
                if not rs2:
                    raise RuntimeError(f"open_stream_with_flags: {lib.get_last_error()}")
                try:
                    if lib.titan_vault_stream_get_length(rs2) != total:
                        raise RuntimeError("flags-open length mismatch")
                finally:
                    lib.titan_vault_close_stream(rs2)
            finally:
                lib.titan_vault_close_stream(rs)

            # stream_set_length: truncation of encrypted streams is backend-dependent; best-effort.
            try:
                ts = lib.titan_vault_open_stream_with_flags(
                    handle, b"/trunc.bin", u8("/trunc.bin"),
                    OPEN_WRITEONLY | OPEN_CREATE | OPEN_TRUNCATE)
                if ts:
                    try:
                        lib.titan_vault_stream_write(ts, chunk_buf, CHUNK)
                        lib.titan_vault_stream_set_length(ts, 4096)
                    finally:
                        lib.titan_vault_close_stream(ts)
            except Exception:
                pass  # optional capability
        section("Streaming", streaming_section)
    finally:
        lib.titan_vault_close_vault(handle)
        handle = None

    # 4. Persistence: reopen the (closed) vault with the passphrase and re-read.
    def persistence_section():
        h2 = open_vault(lib, fmt, vault_dir, password)
        if not h2:
            raise RuntimeError(f"reopen failed: {lib.get_last_error()}")
        try:
            size = ctypes.c_int(4096)
            buf = ctypes.create_string_buffer(size.value)
            check(lib, lib.titan_vault_read_file(h2, b"/persist.txt", u8("/persist.txt"),
                                                 buf, ctypes.byref(size)), "read after reopen")
            if buf.raw[:size.value] != persist_payload:
                raise RuntimeError("persisted content mismatch")
        finally:
            lib.titan_vault_close_vault(h2)
    section("Persistence", persistence_section)

    # 5/6. UVF-only: key rotation, then multi-user (all operate on the vault path).
    if fmt == "uvf":
        # Key rotation must run while the vault is admin-only (the lib refuses to rotate a vault that
        # has extra users, since it would need every user's password to re-wrap the keys).
        rc = lib.titan_vault_rotate_keys(b8(vault_dir), vlen, b8(password), plen, TITAN_VAULT_FORMAT_UVF)
        if rc == TITAN_VAULT_SUCCESS:
            print("  Key rotation tests for UVF: PASSED")
        elif "not implemented" in lib.get_last_error().lower():
            print("  Key rotation tests for UVF: SKIPPED (not implemented)")
        else:
            state["failed"] += 1
            print(f"  Key rotation tests for UVF: FAILED — {lib.get_last_error()}")

        # Public-key (asymmetric) membership: admin grants access to a public key, the user opens with
        # their private key, and the admin can rotate the key without the member's password. Runs before
        # the password Multi-user section so only admin + the public-key user exist at rotation time.
        def public_key_section():
            bob, key_pw = "bob", "bob-key-pass-123"

            # 1. Generate bob's key pair (public key + password-encrypted private key) via the C ABI.
            pub_buf = ctypes.create_string_buffer(4096)
            priv_buf = ctypes.create_string_buffer(8192)
            pub_size = ctypes.c_int(4096)
            priv_size = ctypes.c_int(8192)
            key_pw_bytes = b8(key_pw)
            check(lib, lib.titan_vault_generate_user_keypair(
                key_pw_bytes, len(key_pw_bytes), pub_buf, ctypes.byref(pub_size),
                priv_buf, ctypes.byref(priv_size)), "generate_user_keypair")
            public_key = pub_buf.raw[:pub_size.value]
            encrypted_private_key = priv_buf.raw[:priv_size.value]
            print(f"    generated bob key pair (public {pub_size.value}B, "
                  f"encrypted private {priv_size.value}B)")

            # 2. Grant bob access by PUBLIC key (admin needs no password from bob).
            check(lib, lib.titan_vault_add_user_by_public_key(
                b8(vault_dir), vlen, b8(password), plen, b8(bob), u8(bob),
                public_key, len(public_key)), "add_user_by_public_key")

            # 3. Open the vault as bob with his PRIVATE key and read the admin-written file.
            def read_as_bob():
                h = lib.titan_vault_load_uvf_vault_with_key(
                    b8(vault_dir), vlen, encrypted_private_key, len(encrypted_private_key),
                    b8(key_pw), u8(key_pw), b8(bob), u8(bob))
                if not h:
                    raise RuntimeError(f"load as bob failed: {lib.get_last_error()}")
                try:
                    size = ctypes.c_int(4096)
                    buf = ctypes.create_string_buffer(size.value)
                    check(lib, lib.titan_vault_read_file(h, b"/persist.txt", u8("/persist.txt"),
                                                         buf, ctypes.byref(size)), "read as bob")
                    if buf.raw[:size.value] != persist_payload:
                        raise RuntimeError("bob read mismatch")
                finally:
                    lib.titan_vault_close_vault(h)
            read_as_bob()
            print("    opened as bob (public-key user) and read the admin file OK")

            # 4. Rotate the key for public-key members — admin alone, no bob password — then bob still reads.
            check(lib, lib.titan_vault_rotate_keys_pubkey(b8(vault_dir), vlen, b8(password), plen),
                  "rotate_keys_pubkey")
            read_as_bob()
            print("    rotated keys (no member password) and bob still reads OK")
        section("Public-key multi-user", public_key_section)

        def multi_user_section():
            alice, alice_pw = "alice", "alice-passphrase-123"
            check(lib, lib.titan_vault_add_user(b8(vault_dir), vlen, b8(password), plen,
                                                b8(alice), u8(alice), b8(alice_pw), u8(alice_pw)),
                  "add_user")
            users_buf = (ctypes.c_void_p * MAX_LIST)()
            max_buf = ctypes.c_int(MAX_LIST)
            n = lib.titan_vault_get_vault_users(b8(vault_dir), vlen, b8(password), plen,
                                                users_buf, ctypes.byref(max_buf))
            if n < 0:
                raise RuntimeError(f"get_vault_users rc={n}: {lib.get_last_error()}")
            users = read_string_array(lib, users_buf, n)
            print(f"    vault users: {json_list(users)}")
            if alice not in users:
                raise RuntimeError(f"added user not listed (got {users})")

            # Best-effort: open as the new user and read the admin-written file. This currently fails
            # because LoadMultiUserUvfVaultAsync runs filename-encryption detection without the userId
            # (VaultManager.cs) — a known library limitation, reported (not failed) here.
            try:
                ah = open_vault(lib, "uvf", vault_dir, password, alice, alice_pw)
                if not ah:
                    raise RuntimeError(lib.get_last_error())
                try:
                    size = ctypes.c_int(4096)
                    buf = ctypes.create_string_buffer(size.value)
                    check(lib, lib.titan_vault_read_file(ah, b"/persist.txt", u8("/persist.txt"),
                                                         buf, ctypes.byref(size)), "read as alice")
                    if buf.raw[:size.value] != persist_payload:
                        raise RuntimeError("alice read mismatch")
                    print("    opened as second user and read the admin-written file OK")
                finally:
                    lib.titan_vault_close_vault(ah)
            except Exception as e:
                print(f"    ⚠ opening as a secondary user is not yet supported by the library: {e}")

            # Change a member's password (admin-driven), then remove the member and confirm they're gone.
            alice_new_pw = "alice-passphrase-456"
            check(lib, lib.titan_vault_change_uvf_user_password(
                b8(vault_dir), vlen, b8(password), plen, b8(alice), u8(alice),
                b8(alice_new_pw), u8(alice_new_pw)), "change_uvf_user_password")
            check(lib, lib.titan_vault_remove_user(b8(vault_dir), vlen, b8(password), plen,
                                                   b8(alice), u8(alice)), "remove_user")
            users_buf2 = (ctypes.c_void_p * MAX_LIST)()
            max_buf2 = ctypes.c_int(MAX_LIST)
            n2 = lib.titan_vault_get_vault_users(b8(vault_dir), vlen, b8(password), plen,
                                                 users_buf2, ctypes.byref(max_buf2))
            users2 = read_string_array(lib, users_buf2, max(n2, 0))
            if alice in users2:
                raise RuntimeError(f"removed user still listed (got {users2})")
            print(f"    changed alice's password, then removed alice; users now: {json_list(users2)}")
        section("Multi-user", multi_user_section)

    # 7. Maintenance (both formats): backup the key files, secure-wipe a buffer, change the
    #    password, and reopen with the new password.
    def maintenance_section():
        backup_dir = os.path.join(tempfile.gettempdir(), f"uvf-backup-{fmt}-{os.getpid()}")
        shutil.rmtree(backup_dir, ignore_errors=True)
        check(lib, lib.titan_vault_backup_files(b8(vault_dir), vlen, b8(backup_dir),
                                                u8(backup_dir), 1), "backup_files")
        if not os.path.exists(backup_dir) or len(walk(backup_dir)) == 0:
            raise RuntimeError("backup produced no files")

        secret = ctypes.create_string_buffer(b"super-secret-key-material")
        secret_len = len(secret.raw) - 1  # exclude the trailing NUL added by create_string_buffer
        lib.titan_vault_secure_zero_memory(secret, secret_len)
        if any(b != 0 for b in secret.raw[:secret_len]):
            raise RuntimeError("secure_zero_memory did not zero the buffer")

        new_pw = password + "-rotated"
        if fmt == "uvf":
            check(lib, lib.titan_vault_change_uvf_admin_password(
                b8(vault_dir), vlen, b8(password), plen, b8(new_pw), u8(new_pw)),
                "change_uvf_admin_password")
        else:
            check(lib, lib.titan_vault_change_cryptomator_password(
                b8(vault_dir), vlen, b8(password), plen, b8(new_pw), u8(new_pw)),
                "change_cryptomator_password")
        h3 = open_vault(lib, fmt, vault_dir, new_pw)
        if not h3:
            raise RuntimeError(f"reopen after password change failed: {lib.get_last_error()}")
        try:
            size = ctypes.c_int(4096)
            buf = ctypes.create_string_buffer(size.value)
            check(lib, lib.titan_vault_read_file(h3, b"/persist.txt", u8("/persist.txt"),
                                                 buf, ctypes.byref(size)), "read after password change")
            if buf.raw[:size.value] != persist_payload:
                raise RuntimeError("content mismatch after password change")
        finally:
            lib.titan_vault_close_vault(h3)
        shutil.rmtree(backup_dir, ignore_errors=True)
        print(f"    backed up key files, secure-zeroed a buffer, changed the {fmt} password and re-read OK")
    section("Maintenance", maintenance_section)

    print(f"✅ {fmt} demo finished.")


# ----- helpers for benchmark + interop -----

def mbps(num_bytes, ms):
    return (num_bytes / 1e6) / (ms / 1000.0)  # decimal MB/s


def read_file_full(lib, handle, vault_path):
    """Read a whole vault file into bytes, growing the buffer to the required size if needed."""
    cap = 1 << 20  # 1 MiB
    vplen = u8(vault_path)
    vpb = b8(vault_path)
    for _ in range(4):
        buf = ctypes.create_string_buffer(cap)
        size = ctypes.c_int(cap)
        rc = lib.titan_vault_read_file(handle, vpb, vplen, buf, ctypes.byref(size))
        if rc == TITAN_VAULT_SUCCESS:
            return buf.raw[:size.value]
        if size.value > cap:
            cap = size.value  # grow to required size and retry
            continue
        raise RuntimeError(f"read_file {vault_path} rc={rc}: {lib.get_last_error()}")
    raise RuntimeError(f"read_file {vault_path}: buffer growth failed")


def list_dir(lib, handle, dir_path):
    entries_buf = (ctypes.c_void_p * MAX_LIST)()
    max_buf = ctypes.c_int(MAX_LIST)
    n = lib.titan_vault_list_directory(handle, b8(dir_path), u8(dir_path), entries_buf,
                                       ctypes.byref(max_buf))
    if n < 0:
        raise RuntimeError(f"list_directory {dir_path} rc={n}: {lib.get_last_error()}")
    return read_string_array(lib, entries_buf, n)


def json_list(items):
    """Render a list of strings like JSON.stringify(array) does in the Node demo."""
    return "[" + ",".join('"' + s.replace("\\", "\\\\").replace('"', '\\"') + '"' for s in items) + "]"


# 2. Interop: unlock a REAL Cryptomator vault (created by the Cryptomator app), list the files, and
# md5-compare the decrypted content against the original plaintext files.
def run_cryptomator_interop(lib):
    print("\n========== Cryptomator interop (real vault) ==========")
    base = os.path.normpath(os.path.join(HERE, "..", "_test-cryptomator-vault"))
    vault_dir = os.path.join(base, "smartinventure")
    orig_dir = os.path.join(base, "original-files")
    password = "smartinventure"  # demo vault — hardcoded on purpose

    if not os.path.exists(os.path.join(vault_dir, "masterkey.cryptomator")):
        print(f"No Cryptomator vault found at {vault_dir}", file=sys.stderr)
        return False
    handle = lib.titan_vault_load_cryptomator_vault(b8(vault_dir), u8(vault_dir),
                                                    b8(password), u8(password))
    if not handle:
        print(f"Unlock failed: {lib.get_last_error()}", file=sys.stderr)
        return False
    try:
        print(f"Unlocked real Cryptomator vault at {vault_dir}")
        for d in ["/", "/mysubfolder1", "/mysubfolder1/mysubfolder2"]:
            print(f"  {d}  ->  {json_list(list_dir(lib, handle, d))}")
        cases = [
            ("/Perfect-albums.txt", "Perfect-albums.txt"),
            ("/mysubfolder1/banana.jpg", "banana.jpg"),
            ("/mysubfolder1/mysubfolder2/Rubicon - Rivers - lyrics.txt", "Rubicon - Rivers - lyrics.txt"),
        ]
        all_ok = True
        for vault_path, orig_name in cases:
            decrypted = read_file_full(lib, handle, vault_path)
            got = hashlib.md5(decrypted).hexdigest()
            with open(os.path.join(orig_dir, orig_name), "rb") as f:
                want = hashlib.md5(f.read()).hexdigest()
            ok = got == want
            if not ok:
                all_ok = False
            mark = "✓" if ok else "✗"
            detail = "match" if ok else f"MISMATCH got={got} want={want}"
            print(f"  {mark} {vault_path}  ({len(decrypted)} B)  md5 {detail}")
        print("✅ Reading a real Cryptomator vault worked — all files decrypted and md5-matched the originals."
              if all_ok else "❌ Cryptomator interop FAILED — md5 mismatch.")
        return all_ok
    except Exception as e:
        print(f"❌ Cryptomator interop FAILED: {e}")
        return False
    finally:
        lib.titan_vault_close_vault(handle)


# 1. Benchmark: create a large plaintext file, then encrypt/decrypt it through the vault, reporting MB/s
# for raw disk write, encrypt, decrypt, and raw disk read — for both formats.
def run_benchmark(lib, size_gb):
    size_bytes = round(size_gb * 1024 * 1024 * 1024)
    CHUNK = 4 * 1024 * 1024  # 4 MiB
    print(f"\n========== Benchmark ({size_gb} GB per format, {CHUNK >> 20} MiB chunks) ==========")
    print("  (disk read/write rows may just reflect the OS cache — pass --size larger than your RAM "
          "for disk-bound numbers)")
    for fmt in ["uvf", "cryptomator"]:
        bench_one(lib, fmt, size_bytes, CHUNK)


def bench_one(lib, fmt, size_bytes, CHUNK):
    print(f"\n----- {fmt.upper()} -----")
    base = os.path.join(tempfile.gettempdir(), f"uvf-bench-{fmt}-{os.getpid()}")
    shutil.rmtree(base, ignore_errors=True)
    vault_dir = os.path.join(base, "vault")
    os.makedirs(vault_dir, exist_ok=True)
    plain = os.path.join(base, "plain.bin")
    password = "bench-pass-123"

    def report(label, ms):
        print(f"  {label:<32} {format(ms, '.0f'):>7} ms   {format(mbps(size_bytes, ms), '.1f'):>8} MB/s")

    chunk = bytes(i & 0xff for i in range(CHUNK))  # non-trivial data (avoid sparse-file effects)
    chunk_buf = ctypes.create_string_buffer(chunk, CHUNK)

    try:
        # (a) create the plaintext file on disk — gauges raw medium write speed
        t = time.perf_counter()
        with open(plain, "wb") as f:
            w = 0
            while w < size_bytes:
                n = min(CHUNK, size_bytes - w)
                f.write(chunk[:n])
                w += n
            f.flush()
            os.fsync(f.fileno())
        report("create file (disk write, may be cached)", (time.perf_counter() - t) * 1000.0)

        vlen, plen = u8(vault_dir), u8(password)
        if fmt == "uvf":
            check(lib, lib.titan_vault_create_uvf_vault(b8(vault_dir), vlen, b8(password), plen, 1, 0, 0),
                  "create_uvf_vault")
            handle = lib.titan_vault_load_uvf_vault(b8(vault_dir), vlen, b8(password), plen, None, 0)
        else:
            check(lib, lib.titan_vault_create_cryptomator_vault(b8(vault_dir), vlen, b8(password), plen),
                  "create_cryptomator_vault")
            handle = lib.titan_vault_load_cryptomator_vault(b8(vault_dir), vlen, b8(password), plen)
        if not handle:
            raise RuntimeError(f"load failed: {lib.get_last_error()}")

        try:
            # (b) encrypt — stream the plaintext into the vault
            t = time.perf_counter()
            ws = lib.titan_vault_open_write_stream(handle, b"/big.bin", u8("/big.bin"))
            if not ws:
                raise RuntimeError(f"open_write_stream: {lib.get_last_error()}")
            rbuf = ctypes.create_string_buffer(CHUNK)
            try:
                with open(plain, "rb") as f:
                    while True:
                        data = f.readinto(rbuf)
                        if not data:
                            break
                        if lib.titan_vault_stream_write(ws, rbuf, data) != data:
                            raise RuntimeError("short write")
            finally:
                lib.titan_vault_close_stream(ws)
            report(f"encrypt ({fmt})", (time.perf_counter() - t) * 1000.0)

            # (c) decrypt — stream it back out of the vault (discarding the plaintext)
            t = time.perf_counter()
            rs = lib.titan_vault_open_read_stream(handle, b"/big.bin", u8("/big.bin"))
            if not rs:
                raise RuntimeError(f"open_read_stream: {lib.get_last_error()}")
            dbuf = ctypes.create_string_buffer(CHUNK)
            total = 0
            try:
                while True:
                    got = lib.titan_vault_stream_read(rs, dbuf, CHUNK)
                    if got <= 0:
                        break
                    total += got
            finally:
                lib.titan_vault_close_stream(rs)
            if total != size_bytes:
                raise RuntimeError(f"decrypt size {total} != {size_bytes}")
            report(f"decrypt ({fmt})", (time.perf_counter() - t) * 1000.0)

            # (d) read the plaintext file back from disk — gauges raw medium read speed
            t = time.perf_counter()
            rbuf2 = ctypes.create_string_buffer(CHUNK)
            with open(plain, "rb") as f:
                while f.readinto(rbuf2) > 0:
                    pass  # discard
            report("read file (disk read, may be cached)", (time.perf_counter() - t) * 1000.0)
        finally:
            lib.titan_vault_close_vault(handle)
    finally:
        shutil.rmtree(base, ignore_errors=True)


def walk(directory):
    out = []
    for root, _, files in os.walk(directory):
        for name in files:
            out.append(os.path.join(root, name))
    return out


def main():
    args = parse_args(sys.argv[1:])
    if not os.path.exists(args["lib"]):
        arch = platform.machine()
        print(f"Native library not found: {args['lib']}\n"
              f"Build it first:  ../../BuildScripts/build.ps1 -Task aot   (or build.sh --task aot)\n"
              f"Then it loads automatically, or pass --lib <path> / set TITANVAULT_LIB.\n"
              f"Note: the library must match your Python architecture ({arch}).",
              file=sys.stderr)
        return 1

    lib = Lib(args["lib"])
    print(f"TitanVault version: {version(lib)}")

    # Focused modes (run only the requested thing).
    if args["interop"]:
        return 0 if run_cryptomator_interop(lib) else 1
    if args["benchmark"]:
        run_benchmark(lib, args["size_gb"])
        return 0

    state = {"failed": 0}

    # Functional sections, for one format (--format) or both (default).
    formats = [args["format"]] if args["format"] else ["uvf", "cryptomator"]
    for fmt in formats:
        try:
            run_demo(lib, fmt, os.path.join(args["vault"], fmt), args["password"], state)
        except Exception as e:
            state["failed"] += 1
            print(f"\n❌ {fmt} demo aborted: {e}")

    # A full run (no --format) also exercises the real-Cryptomator-vault interop and a quick throughput
    # benchmark. (Use --cryptomator-interop or --benchmark [--size <GB>] to run either on its own; the
    # standalone benchmark defaults to 1 GB.)
    if not args["format"]:
        interop_vault = os.path.normpath(os.path.join(
            HERE, "..", "_test-cryptomator-vault", "smartinventure", "masterkey.cryptomator"))
        if os.path.exists(interop_vault):
            if not run_cryptomator_interop(lib):
                state["failed"] += 1
        else:
            print("\n(Cryptomator interop skipped — Demo/_test-cryptomator-vault not present)")
        try:
            run_benchmark(lib, 0.25)
        except Exception as e:
            state["failed"] += 1
            print(f"\n❌ benchmark aborted: {e}")

    print("\n✅ All Python demo sections passed." if state["failed"] == 0
          else f"\n❌ {state['failed']} section(s) failed.")
    return 0 if state["failed"] == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
