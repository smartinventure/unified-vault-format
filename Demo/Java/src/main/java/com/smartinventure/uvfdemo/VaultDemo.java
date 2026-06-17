package com.smartinventure.uvfdemo;

import com.sun.jna.Library;
import com.sun.jna.Native;
import com.sun.jna.Pointer;
import com.sun.jna.ptr.IntByReference;

import java.io.File;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.Arrays;

/**
 * UVF / Cryptomator demo in Java via the native TitanVault library (C ABI, using JNA).
 *
 * Build the native library first (from the repo root):
 *   dotnet publish Uvf.Net/UvfLib.Master/UvfLib.Master.csproj -c Release -r win-x64 -p:PublishAot=true
 *   (produces TitanVault.dll / libTitanVault.so / libTitanVault.dylib)
 *
 * Run:
 *   mvn -q compile exec:java -Dexec.args="--lib /path/to/TitanVault.dll --format uvf"
 */
public final class VaultDemo {

    private static final int TITAN_VAULT_SUCCESS = 0;

    /** JNA mapping of the native C ABI. Strings are passed as UTF-8 bytes + explicit length. */
    public interface TitanVault extends Library {
        Pointer titan_vault_get_version();
        Pointer titan_vault_get_last_error();          // static buffer — do NOT free
        void titan_vault_free_string(Pointer p);

        int titan_vault_create_uvf_vault(byte[] path, int pathLen, byte[] pwd, int pwdLen,
                                         int encryptFilenames, int kdfMethod, int kdfIterations);
        Pointer titan_vault_load_uvf_vault(byte[] path, int pathLen, byte[] pwd, int pwdLen,
                                           byte[] userId, int userIdLen);
        int titan_vault_create_cryptomator_vault(byte[] path, int pathLen, byte[] pwd, int pwdLen);
        Pointer titan_vault_load_cryptomator_vault(byte[] path, int pathLen, byte[] pwd, int pwdLen);

        int titan_vault_write_file(Pointer handle, byte[] path, int pathLen, byte[] buffer, int bufferSize);
        int titan_vault_read_file(Pointer handle, byte[] path, int pathLen, byte[] buffer, IntByReference bufferSize);
        int titan_vault_file_exists(Pointer handle, byte[] path, int pathLen);
        int titan_vault_delete_file(Pointer handle, byte[] path, int pathLen);
        int titan_vault_close_vault(Pointer handle);
    }

    public static void main(String[] args) throws Exception {
        String libPath = envOrDefault("TITANVAULT_LIB", "./TitanVault.dll");
        String format = "uvf";
        String vaultDir = new File(System.getProperty("java.io.tmpdir"), "uvf-java-demo").getAbsolutePath();
        String password = "correct horse battery staple";
        for (int i = 0; i < args.length - 1; i++) {
            switch (args[i]) {
                case "--lib": libPath = args[++i]; break;
                case "--format": format = args[++i]; break;
                case "--vault": vaultDir = args[++i]; break;
                case "--password": password = args[++i]; break;
                default: break;
            }
        }

        TitanVault lib = Native.load(libPath, TitanVault.class);
        System.out.println("TitanVault version: " + readAndFree(lib, lib.titan_vault_get_version()));

        Files.createDirectories(Path.of(vaultDir));
        byte[] vpath = utf8(vaultDir);
        byte[] pwd = utf8(password);

        // 1. Create + open the vault.
        Pointer handle;
        if (format.equals("uvf")) {
            check(lib, lib.titan_vault_create_uvf_vault(vpath, vpath.length, pwd, pwd.length, 1, 0, 0), "create_uvf_vault");
            handle = lib.titan_vault_load_uvf_vault(vpath, vpath.length, pwd, pwd.length, null, 0);
        } else {
            check(lib, lib.titan_vault_create_cryptomator_vault(vpath, vpath.length, pwd, pwd.length), "create_cryptomator_vault");
            handle = lib.titan_vault_load_cryptomator_vault(vpath, vpath.length, pwd, pwd.length);
        }
        if (handle == null || Pointer.nativeValue(handle) == 0) {
            throw new RuntimeException("load vault failed: " + lastError(lib));
        }
        System.out.println("Created + opened " + format + " vault at " + vaultDir);

        try {
            byte[] fpath = utf8("/hello.txt");
            byte[] plaintext = "Hello, encrypted world!".getBytes(StandardCharsets.UTF_8);

            // 2. Encrypt: write a file.
            check(lib, lib.titan_vault_write_file(handle, fpath, fpath.length, plaintext, plaintext.length), "write_file");
            System.out.println("Wrote /hello.txt (" + plaintext.length + " bytes)");

            // 3. Decrypt: read it back (in/out buffer size).
            byte[] buf = new byte[64 * 1024];
            IntByReference size = new IntByReference(buf.length);
            check(lib, lib.titan_vault_read_file(handle, fpath, fpath.length, buf, size), "read_file");
            byte[] data = Arrays.copyOf(buf, size.getValue());
            System.out.println("Read back (decrypted): \"" + new String(data, StandardCharsets.UTF_8) + "\"");
            if (!Arrays.equals(data, plaintext)) throw new RuntimeException("round-trip mismatch!");

            // 4. Cleartext name is not on disk.
            boolean leaked = Files.walk(Path.of(vaultDir)).anyMatch(p -> p.getFileName().toString().equals("hello.txt"));
            System.out.println("Backend stores plaintext name 'hello.txt'? " + leaked + " (expected: false)");

            // 5. Exists + delete.
            System.out.println("file_exists(/hello.txt) = " + lib.titan_vault_file_exists(handle, fpath, fpath.length) + " (1 == yes)");
            check(lib, lib.titan_vault_delete_file(handle, fpath, fpath.length), "delete_file");
            System.out.println("after delete, file_exists = " + lib.titan_vault_file_exists(handle, fpath, fpath.length) + " (0 == no)");
        } finally {
            lib.titan_vault_close_vault(handle);
        }
        System.out.println("✅ Java demo completed.");
    }

    private static byte[] utf8(String s) { return s.getBytes(StandardCharsets.UTF_8); }

    private static String lastError(TitanVault lib) {
        Pointer p = lib.titan_vault_get_last_error();
        return p == null ? "(no error message)" : p.getString(0, "UTF-8");
    }

    private static String readAndFree(TitanVault lib, Pointer p) {
        if (p == null || Pointer.nativeValue(p) == 0) return "(unknown)";
        try { return p.getString(0, "UTF-8"); }
        finally { lib.titan_vault_free_string(p); }
    }

    private static void check(TitanVault lib, int rc, String what) {
        if (rc != TITAN_VAULT_SUCCESS) throw new RuntimeException(what + " failed (rc=" + rc + "): " + lastError(lib));
    }

    private static String envOrDefault(String key, String def) {
        String v = System.getenv(key);
        return (v == null || v.isEmpty()) ? def : v;
    }

    private VaultDemo() { }
}
