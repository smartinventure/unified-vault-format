#!/usr/bin/env python3
"""
UVF / Cryptomator demo in Python via the native TitanVault library (C ABI, ctypes — no extra deps).

It loads the native library, creates a vault, writes a file into it (encrypted), reads it back
(decrypted), checks existence, deletes it, and closes the vault — for UVF (v3) or Cryptomator (v8).

Build the native library first (from the repo root):
    dotnet publish Uvf.Net/UvfLib.Master/UvfLib.Master.csproj -c Release -r win-x64 -p:PublishAot=true
    # produces TitanVault.dll (win) / libTitanVault.so (linux) / libTitanVault.dylib (macOS)

Run:
    python vault_demo.py --lib /path/to/TitanVault.dll --format uvf
"""

import argparse
import ctypes
import os
import sys
import tempfile

TITAN_VAULT_SUCCESS = 0


def utf8(s: str):
    """Return (bytes, length) for a UTF-8 string (pointers are ptr+len, not NUL-terminated)."""
    b = s.encode("utf-8")
    return b, len(b)


def load_library(path: str) -> ctypes.CDLL:
    lib = ctypes.CDLL(path)

    # const char* titan_vault_get_version()            (must be freed with titan_vault_free_string)
    lib.titan_vault_get_version.restype = ctypes.c_void_p
    # const char* titan_vault_get_last_error()          (static buffer — do NOT free)
    lib.titan_vault_get_last_error.restype = ctypes.c_char_p
    lib.titan_vault_free_string.argtypes = [ctypes.c_void_p]

    # int titan_vault_create_uvf_vault(path,len, adminPwd,len, encryptFilenames, kdfMethod, kdfIters)
    lib.titan_vault_create_uvf_vault.restype = ctypes.c_int
    lib.titan_vault_create_uvf_vault.argtypes = [
        ctypes.c_char_p, ctypes.c_int, ctypes.c_char_p, ctypes.c_int,
        ctypes.c_int, ctypes.c_int, ctypes.c_int,
    ]
    # void* titan_vault_load_uvf_vault(path,len, pwd,len, userId,len)
    lib.titan_vault_load_uvf_vault.restype = ctypes.c_void_p
    lib.titan_vault_load_uvf_vault.argtypes = [
        ctypes.c_char_p, ctypes.c_int, ctypes.c_char_p, ctypes.c_int, ctypes.c_char_p, ctypes.c_int,
    ]
    # int titan_vault_create_cryptomator_vault(path,len, pwd,len)
    lib.titan_vault_create_cryptomator_vault.restype = ctypes.c_int
    lib.titan_vault_create_cryptomator_vault.argtypes = [
        ctypes.c_char_p, ctypes.c_int, ctypes.c_char_p, ctypes.c_int,
    ]
    # void* titan_vault_load_cryptomator_vault(path,len, pwd,len)
    lib.titan_vault_load_cryptomator_vault.restype = ctypes.c_void_p
    lib.titan_vault_load_cryptomator_vault.argtypes = [
        ctypes.c_char_p, ctypes.c_int, ctypes.c_char_p, ctypes.c_int,
    ]
    # int titan_vault_write_file(handle, path,len, buffer, bufferSize)
    lib.titan_vault_write_file.restype = ctypes.c_int
    lib.titan_vault_write_file.argtypes = [
        ctypes.c_void_p, ctypes.c_char_p, ctypes.c_int, ctypes.c_void_p, ctypes.c_int,
    ]
    # int titan_vault_read_file(handle, path,len, buffer, &bufferSize)
    lib.titan_vault_read_file.restype = ctypes.c_int
    lib.titan_vault_read_file.argtypes = [
        ctypes.c_void_p, ctypes.c_char_p, ctypes.c_int, ctypes.c_void_p, ctypes.POINTER(ctypes.c_int),
    ]
    # int titan_vault_file_exists(handle, path,len)   -> 1 exists / 0 not / <0 error
    lib.titan_vault_file_exists.restype = ctypes.c_int
    lib.titan_vault_file_exists.argtypes = [ctypes.c_void_p, ctypes.c_char_p, ctypes.c_int]
    # int titan_vault_delete_file(handle, path,len)
    lib.titan_vault_delete_file.restype = ctypes.c_int
    lib.titan_vault_delete_file.argtypes = [ctypes.c_void_p, ctypes.c_char_p, ctypes.c_int]
    # int titan_vault_close_vault(handle)
    lib.titan_vault_close_vault.restype = ctypes.c_int
    lib.titan_vault_close_vault.argtypes = [ctypes.c_void_p]
    return lib


def last_error(lib) -> str:
    msg = lib.titan_vault_get_last_error()
    return msg.decode("utf-8") if msg else "(no error message)"


def check(lib, rc: int, what: str):
    if rc != TITAN_VAULT_SUCCESS:
        raise RuntimeError(f"{what} failed (rc={rc}): {last_error(lib)}")


def version(lib) -> str:
    ptr = lib.titan_vault_get_version()
    if not ptr:
        return "(unknown)"
    try:
        return ctypes.cast(ptr, ctypes.c_char_p).value.decode("utf-8")
    finally:
        lib.titan_vault_free_string(ptr)


def main() -> int:
    ap = argparse.ArgumentParser(description="UVF/Cryptomator Python demo (native TitanVault).")
    ap.add_argument("--lib", default=os.environ.get("TITANVAULT_LIB", "./TitanVault.dll"),
                    help="Path to the native library (TitanVault.dll / libTitanVault.so / .dylib).")
    ap.add_argument("--format", choices=["uvf", "cryptomator"], default="uvf")
    ap.add_argument("--vault", default=os.path.join(tempfile.gettempdir(), "uvf-python-demo"))
    ap.add_argument("--password", default="correct horse battery staple")
    args = ap.parse_args()

    lib = load_library(args.lib)
    print(f"TitanVault version: {version(lib)}")

    os.makedirs(args.vault, exist_ok=True)
    vpath, vlen = utf8(args.vault)
    pwd, plen = utf8(args.password)

    # 1. Create the vault.
    if args.format == "uvf":
        check(lib, lib.titan_vault_create_uvf_vault(vpath, vlen, pwd, plen, 1, 0, 0), "create_uvf_vault")
        handle = lib.titan_vault_load_uvf_vault(vpath, vlen, pwd, plen, None, 0)
    else:
        check(lib, lib.titan_vault_create_cryptomator_vault(vpath, vlen, pwd, plen), "create_cryptomator_vault")
        handle = lib.titan_vault_load_cryptomator_vault(vpath, vlen, pwd, plen)
    if not handle:
        raise RuntimeError(f"load vault failed: {last_error(lib)}")
    print(f"Created + opened {args.format} vault at {args.vault}")

    try:
        fpath, fplen = utf8("/hello.txt")
        plaintext = b"Hello, encrypted world!"

        # 2. Encrypt: write a file into the vault.
        check(lib, lib.titan_vault_write_file(handle, fpath, fplen, plaintext, len(plaintext)), "write_file")
        print(f"Wrote /hello.txt ({len(plaintext)} bytes)")

        # 3. Decrypt: read it back. read_file takes an in/out buffer size.
        size = ctypes.c_int(64 * 1024)
        buf = ctypes.create_string_buffer(size.value)
        check(lib, lib.titan_vault_read_file(handle, fpath, fplen, buf, ctypes.byref(size)), "read_file")
        data = buf.raw[:size.value]
        print(f"Read back (decrypted): {data!r}")
        assert data == plaintext, "round-trip mismatch!"

        # 4. The cleartext name is not on disk (vault stores ciphertext + encrypted names).
        leaked = any("hello.txt" in files for _, _, files in os.walk(args.vault))
        print(f"Backend stores plaintext name 'hello.txt'? {leaked} (expected: False)")

        # 5. Exists + delete.
        print(f"file_exists(/hello.txt) = {lib.titan_vault_file_exists(handle, fpath, fplen)} (1 == yes)")
        check(lib, lib.titan_vault_delete_file(handle, fpath, fplen), "delete_file")
        print(f"after delete, file_exists = {lib.titan_vault_file_exists(handle, fpath, fplen)} (0 == no)")
    finally:
        lib.titan_vault_close_vault(handle)

    print("✅ Python demo completed.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
