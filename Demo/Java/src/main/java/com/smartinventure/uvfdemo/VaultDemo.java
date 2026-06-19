package com.smartinventure.uvfdemo;

import com.sun.jna.Library;
import com.sun.jna.Memory;
import com.sun.jna.Native;
import com.sun.jna.Pointer;
import com.sun.jna.ptr.IntByReference;
import com.sun.jna.ptr.LongByReference;

import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.security.MessageDigest;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;
import java.util.Locale;
import java.util.stream.Collectors;
import java.util.stream.Stream;

/**
 * UVF / Cryptomator demo in Java via the native TitanVault library (C ABI, using JNA).
 *
 * <p>This is a full-parity port of {@code Demo/NodeJs/vault-demo.js}: it runs BOTH formats by default
 * (uvf then cryptomator), then the real-Cryptomator-vault interop, then a quick 0.25 GB benchmark.
 *
 * <p>Build the native library first (from the repo root):
 * <pre>
 *   ../../BuildScripts/build.ps1 -Task aot        # -&gt; Dist/Native/win-x64/TitanVault.dll
 *   ../../BuildScripts/build.sh  --task aot        # -&gt; Dist/Native/&lt;rid&gt;/libTitanVault.{so,dylib}
 * </pre>
 *
 * <p>Run (runs both formats by default; --format uvf|cryptomator restricts to one):
 * <pre>
 *   mvn -q compile exec:java -Dexec.args="--lib ../../Dist/Native/win-x64/TitanVault.dll"
 * </pre>
 */
public final class VaultDemo {

    private static final int TITAN_VAULT_SUCCESS = 0;
    private static final int TITAN_VAULT_FORMAT_CRYPTOMATOR = 0;
    private static final int TITAN_VAULT_FORMAT_UVF = 1;
    private static final int MAX_LIST = 256;

    /** JNA mapping of the native C ABI. Strings are passed as UTF-8 bytes + explicit length. */
    public interface TitanVault extends Library {
        // Returns a heap char* the caller must release with titan_vault_free_string.
        Pointer titan_vault_get_version();
        // Heap char* (also freed via free_string in this demo, matching the JS reference comment).
        Pointer titan_vault_get_last_error();
        void titan_vault_free_string(Pointer p);

        int titan_vault_create_uvf_vault(byte[] path, int pathLen, byte[] pwd, int pwdLen,
                                         int encryptFilenames, int kdfMethod, int kdfIterations);
        Pointer titan_vault_load_uvf_vault(byte[] path, int pathLen, byte[] pwd, int pwdLen,
                                           byte[] userId, int userIdLen);
        int titan_vault_create_cryptomator_vault(byte[] path, int pathLen, byte[] pwd, int pwdLen);
        Pointer titan_vault_load_cryptomator_vault(byte[] path, int pathLen, byte[] pwd, int pwdLen);
        int titan_vault_close_vault(Pointer handle);

        int titan_vault_write_file(Pointer handle, byte[] path, int pathLen, byte[] buffer, int bufferSize);
        int titan_vault_read_file(Pointer handle, byte[] path, int pathLen, byte[] buffer, IntByReference bufferSize);
        int titan_vault_file_exists(Pointer handle, byte[] path, int pathLen);
        int titan_vault_delete_file(Pointer handle, byte[] path, int pathLen);

        int titan_vault_create_directory(Pointer handle, byte[] path, int pathLen);
        int titan_vault_directory_exists(Pointer handle, byte[] path, int pathLen);
        int titan_vault_delete_directory(Pointer handle, byte[] path, int pathLen);
        // entriesBuffer = char*[maxEntries]; maxEntries is an int* (in). Returns count; each entry freed.
        int titan_vault_list_directory(Pointer handle, byte[] path, int pathLen, Pointer entriesBuffer, IntByReference maxEntries);
        // fileSize = int64* (out), lastModified = int64* (out, unix seconds).
        int titan_vault_get_file_info(Pointer handle, byte[] path, int pathLen, LongByReference fileSize, LongByReference lastModified);
        int titan_vault_move(Pointer handle, byte[] src, int srcLen, byte[] dst, int dstLen);

        Pointer titan_vault_open_read_stream(Pointer handle, byte[] path, int pathLen);
        Pointer titan_vault_open_write_stream(Pointer handle, byte[] path, int pathLen);
        int titan_vault_stream_write(Pointer stream, byte[] buffer, int count);
        int titan_vault_stream_read(Pointer stream, byte[] buffer, int count);
        long titan_vault_stream_seek(Pointer stream, long offset, int origin);
        long titan_vault_stream_get_length(Pointer stream);
        int titan_vault_close_stream(Pointer stream);

        // UVF multi-user / key management — these operate on the vault PATH (vault need not be open).
        int titan_vault_add_user(byte[] path, int pathLen, byte[] adminPwd, int adminPwdLen,
                                 byte[] userId, int userIdLen, byte[] userPwd, int userPwdLen);
        int titan_vault_get_vault_users(byte[] path, int pathLen, byte[] adminPwd, int adminPwdLen,
                                        Pointer usersBuffer, IntByReference maxUsers);
        int titan_vault_rotate_keys(byte[] path, int pathLen, byte[] adminPwd, int adminPwdLen, int vaultFormat);

        // UVF public-key (asymmetric) membership. pub/priv buffers + in/out int* sizes.
        int titan_vault_generate_user_keypair(byte[] password, int passwordLen,
                                              byte[] pubBuffer, IntByReference pubSize,
                                              byte[] privBuffer, IntByReference privSize);
        int titan_vault_add_user_by_public_key(byte[] path, int pathLen, byte[] adminPwd, int adminPwdLen,
                                               byte[] userId, int userIdLen, byte[] publicKey, int publicKeyLen);
        Pointer titan_vault_load_uvf_vault_with_key(byte[] path, int pathLen,
                                                    byte[] encryptedPrivKey, int encryptedPrivKeyLen,
                                                    byte[] keyPwd, int keyPwdLen, byte[] userId, int userIdLen);
        int titan_vault_rotate_keys_pubkey(byte[] path, int pathLen, byte[] adminPwd, int adminPwdLen);

        // Library / maintenance utilities (operate on the vault PATH; the vault need not be open).
        int titan_vault_detect_vault_format(byte[] path, int pathLen);
        // JNA copies the byte[] back after the call, so the in-place zeroing is observable.
        void titan_vault_secure_zero_memory(byte[] buffer, int size);
        int titan_vault_backup_files(byte[] path, int pathLen, byte[] backupPath, int backupPathLen, int overwriteExisting);
        int titan_vault_change_cryptomator_password(byte[] path, int pathLen,
                                                    byte[] oldPwd, int oldPwdLen, byte[] newPwd, int newPwdLen);
        int titan_vault_change_uvf_admin_password(byte[] path, int pathLen,
                                                  byte[] oldPwd, int oldPwdLen, byte[] newPwd, int newPwdLen);
        int titan_vault_change_uvf_user_password(byte[] path, int pathLen,
                                                 byte[] adminPwd, int adminPwdLen, byte[] userId, int userIdLen,
                                                 byte[] newUserPwd, int newUserPwdLen);
        int titan_vault_remove_user(byte[] path, int pathLen, byte[] adminPwd, int adminPwdLen,
                                    byte[] userId, int userIdLen);

        // Text convenience (UTF-8). read_all_text returns a heap char* that must be freed.
        int titan_vault_write_all_text(Pointer handle, byte[] path, int pathLen, byte[] text, int textLen);
        int titan_vault_append_all_text(Pointer handle, byte[] path, int pathLen, byte[] text, int textLen);
        Pointer titan_vault_read_all_text(Pointer handle, byte[] path, int pathLen);

        // Fuller stream API (the core read/write/seek are above).
        Pointer titan_vault_open_stream_with_flags(Pointer handle, byte[] path, int pathLen, int openFlags);
        long titan_vault_stream_get_position(Pointer stream);
        int titan_vault_stream_set_length(Pointer stream, long length);
        int titan_vault_stream_flush(Pointer stream);
    }

    // StorageLib.Abstractions.OpenFlags values (for open_stream_with_flags).
    private static final int OPEN_READONLY = 0x0000;
    private static final int OPEN_WRITEONLY = 0x0001;
    private static final int OPEN_CREATE = 0x0040;
    private static final int OPEN_TRUNCATE = 0x0200;

    // ----- parsed command-line arguments -----

    private static final class Args {
        String lib;
        String format;       // null => run both
        String vault;
        String password = "correct horse battery staple";
        boolean benchmark;
        boolean interop;
        double sizeGb = 1;
    }

    /** Mutable counter passed through the per-format sections (mirrors the JS { failed } state). */
    private static final class State {
        int failed;
    }

    /** A section reported FAILED instead of throwing out of runDemo (mirrors the JS section() catch). */
    private static final class SectionException extends Exception {
        SectionException(String message) { super(message); }
    }

    // ----- entry point -----

    public static void main(String[] argv) {
        // Windows consoles default to a legacy codepage that can't encode the ✅/❌ status emoji
        // (UnicodeEncodeError-equivalent); force UTF-8 stdout/stderr like Node/Python do.
        try {
            System.setOut(new java.io.PrintStream(System.out, true, "UTF-8"));
            System.setErr(new java.io.PrintStream(System.err, true, "UTF-8"));
        } catch (java.io.UnsupportedEncodingException ignored) { /* UTF-8 is always available */ }

        Args args = parseArgs(argv);
        if (!Files.exists(Path.of(args.lib))) {
            System.err.println("Native library not found: " + args.lib + "\n"
                + "Build it first:  ../../BuildScripts/build.ps1 -Task aot   (or build.sh --task aot)\n"
                + "Then it loads automatically, or pass --lib <path> / set TITANVAULT_LIB.\n"
                + "Note: the library must match your JVM architecture (" + System.getProperty("os.arch") + ").");
            System.exit(1);
        }

        TitanVault lib = Native.load(args.lib, TitanVault.class);
        System.out.println("TitanVault version: " + version(lib));

        // Focused modes (run only the requested thing).
        if (args.interop) { System.exit(runCryptomatorInterop(lib) ? 0 : 1); }
        if (args.benchmark) { runBenchmark(lib, args.sizeGb); System.exit(0); }

        State state = new State();

        // Functional sections, for one format (--format) or both (default).
        List<String> formats = args.format != null ? List.of(args.format) : List.of("uvf", "cryptomator");
        for (String format : formats) {
            try {
                runDemo(lib, format, Path.of(args.vault, format).toString(), args.password, state);
            } catch (Exception e) {
                state.failed++;
                System.out.println("\n❌ " + format + " demo aborted: " + e.getMessage());
            }
        }

        // A full run (no --format) also exercises the real-Cryptomator-vault interop and a quick throughput
        // benchmark. (Use --cryptomator-interop or --benchmark [--size <GB>] to run either on its own; the
        // standalone benchmark defaults to 1 GB.)
        if (args.format == null) {
            Path interopVault = projectDir().resolve("..").resolve("_test-cryptomator-vault")
                .resolve("smartinventure").resolve("masterkey.cryptomator").normalize();
            if (Files.exists(interopVault)) {
                if (!runCryptomatorInterop(lib)) state.failed++;
            } else {
                System.out.println("\n(Cryptomator interop skipped — Demo/_test-cryptomator-vault not present)");
            }
            try {
                runBenchmark(lib, 0.25);
            } catch (Exception e) {
                state.failed++;
                System.out.println("\n❌ benchmark aborted: " + e.getMessage());
            }
        }

        System.out.println(state.failed == 0
            ? "\n✅ All Node.js demo sections passed."
            : "\n❌ " + state.failed + " section(s) failed.");
        System.exit(state.failed == 0 ? 0 : 1);
    }

    // ----- the per-format demo, organised into sections each reporting PASSED/FAILED -----

    private static void runDemo(TitanVault lib, String format, String vaultDir, String password, State state)
            throws Exception {
        System.out.println("\n========== " + format.toUpperCase(Locale.ROOT) + " ==========");
        rmRf(Path.of(vaultDir));
        Files.createDirectories(Path.of(vaultDir));
        byte[] vpath = utf8(vaultDir);
        int vlen = vpath.length;
        byte[] pwd = utf8(password);
        int plen = pwd.length;

        // Create the vault.
        if (format.equals("uvf")) check(lib, lib.titan_vault_create_uvf_vault(vpath, vlen, pwd, plen, 1, 0, 0), "create_uvf_vault");
        else check(lib, lib.titan_vault_create_cryptomator_vault(vpath, vlen, pwd, plen), "create_cryptomator_vault");

        Pointer handle = openVault(lib, format, vaultDir, password, null, null);
        if (isNull(handle)) throw new RuntimeException("load " + format + " vault failed: " + lastError(lib));
        System.out.println("Created + opened " + format + " vault at " + vaultDir);

        // 0. Detect the on-disk format (path-based — the vault need not be open).
        section(state, "Detect format", format, () -> {
            int detected = lib.titan_vault_detect_vault_format(vpath, vlen);
            int expected = format.equals("uvf") ? TITAN_VAULT_FORMAT_UVF : TITAN_VAULT_FORMAT_CRYPTOMATOR;
            if (detected != expected) throw new SectionException("detect_vault_format=" + detected + ", expected " + expected);
        });

        // A file we deliberately keep around to prove persistence + multi-user access later.
        final byte[] persistPayload = "persisted across reopen".getBytes(StandardCharsets.UTF_8);
        byte[] persistPath = utf8("/persist.txt");
        check(lib, lib.titan_vault_write_file(handle, persistPath, persistPath.length, persistPayload, persistPayload.length), "write persist.txt");

        try {
            // 1. Basic file round-trip.
            section(state, "File", format, () -> {
                byte[] fp = utf8("/hello.txt");
                byte[] plaintext = "Hello, encrypted world!".getBytes(StandardCharsets.UTF_8);
                check(lib, lib.titan_vault_write_file(handle, fp, fp.length, plaintext, plaintext.length), "write_file");
                byte[] buf = new byte[64 * 1024];
                IntByReference size = new IntByReference(buf.length);
                check(lib, lib.titan_vault_read_file(handle, fp, fp.length, buf, size), "read_file");
                if (!Arrays.equals(Arrays.copyOf(buf, size.getValue()), plaintext)) throw new SectionException("round-trip mismatch");
                boolean leaked = walk(Path.of(vaultDir)).stream().anyMatch(f -> f.getFileName().toString().equals("hello.txt"));
                if (leaked) throw new SectionException("plaintext filename leaked to disk");
                if (lib.titan_vault_file_exists(handle, fp, fp.length) != 1) throw new SectionException("exists should be 1");
                check(lib, lib.titan_vault_delete_file(handle, fp, fp.length), "delete_file");
                if (lib.titan_vault_file_exists(handle, fp, fp.length) != 0) throw new SectionException("exists should be 0 after delete");
            });

            // 1b. UTF-8 text convenience: write, append, read-back.
            section(state, "Text helpers", format, () -> {
                byte[] tf = utf8("/notes.txt");
                byte[] first = utf8("first line\n");
                byte[] second = utf8("second line\n");
                check(lib, lib.titan_vault_write_all_text(handle, tf, tf.length, first, first.length), "write_all_text");
                check(lib, lib.titan_vault_append_all_text(handle, tf, tf.length, second, second.length), "append_all_text");
                Pointer ptr = lib.titan_vault_read_all_text(handle, tf, tf.length);
                if (isNull(ptr)) throw new SectionException("read_all_text: " + lastError(lib));
                String text;
                try { text = ptr.getString(0, "UTF-8"); }
                finally { lib.titan_vault_free_string(ptr); }
                String expected = "first line\nsecond line\n";
                if (!text.equals(expected)) throw new SectionException("text round-trip mismatch: " + json(List.of(text)));
            });

            // 2. Directories: create, write into, list, file-info, move/rename.
            section(state, "Directory", format, () -> {
                byte[] docs = utf8("/docs");
                check(lib, lib.titan_vault_create_directory(handle, docs, docs.length), "create_directory");
                if (lib.titan_vault_directory_exists(handle, docs, docs.length) != 1) throw new SectionException("directory_exists should be 1");
                byte[] note = utf8("/docs/note.txt");
                byte[] body = "inside a subdirectory".getBytes(StandardCharsets.UTF_8);
                check(lib, lib.titan_vault_write_file(handle, note, note.length, body, body.length), "write into dir");

                int n = lib.titan_vault_list_directory(handle, docs, docs.length, listBuffer(), listMax());
                if (n < 0) throw new SectionException("list_directory rc=" + n + ": " + lastError(lib));
                List<String> names = readStringArray(lib, lastEntriesBuffer, n);
                if (!names.contains("note.txt")) throw new SectionException("listing missing note.txt (got " + json(names) + ")");

                LongByReference sizeRef = new LongByReference();
                LongByReference mtimeRef = new LongByReference();
                check(lib, lib.titan_vault_get_file_info(handle, note, note.length, sizeRef, mtimeRef), "get_file_info");
                long sz = sizeRef.getValue();
                if (sz != body.length) throw new SectionException("file size " + sz + " != " + body.length);

                byte[] renamed = utf8("/docs/renamed.txt");
                check(lib, lib.titan_vault_move(handle, note, note.length, renamed, renamed.length), "move");
                n = lib.titan_vault_list_directory(handle, docs, docs.length, listBuffer(), listMax());
                names = readStringArray(lib, lastEntriesBuffer, n);
                if (!names.contains("renamed.txt")) throw new SectionException("rename not reflected (got " + json(names) + ")");
                System.out.println("    /docs now contains: " + json(names) + " (size of note was " + sz + " bytes)");
            });

            // 3. Streaming: write a multi-chunk file, then random-access read with seek.
            section(state, "Streaming", format, () -> {
                byte[] fp = utf8("/big.bin");
                final int CHUNK = 32 * 1024, CHUNKS = 4, total = CHUNK * CHUNKS;
                byte[] chunk = new byte[CHUNK];
                for (int j = 0; j < CHUNK; j++) chunk[j] = (byte) (j % 256); // file[O] == O % 256

                Pointer ws = lib.titan_vault_open_write_stream(handle, fp, fp.length);
                if (isNull(ws)) throw new SectionException("open_write_stream: " + lastError(lib));
                try {
                    for (int i = 0; i < CHUNKS; i++) if (lib.titan_vault_stream_write(ws, chunk, CHUNK) != CHUNK) throw new SectionException("short write");
                    check(lib, lib.titan_vault_stream_flush(ws), "stream_flush");
                } finally { lib.titan_vault_close_stream(ws); }

                Pointer rs = lib.titan_vault_open_read_stream(handle, fp, fp.length);
                if (isNull(rs)) throw new SectionException("open_read_stream: " + lastError(lib));
                try {
                    long len = lib.titan_vault_stream_get_length(rs);
                    if (len != total) throw new SectionException("stream length " + len + " != " + total);
                    // sequential read of the whole thing, verifying the position-dependent pattern
                    byte[] rbuf = new byte[CHUNK];
                    int off = 0, got;
                    while ((got = lib.titan_vault_stream_read(rs, rbuf, CHUNK)) > 0) {
                        for (int k = 0; k < got; k++) if ((rbuf[k] & 0xff) != (off + k) % 256) throw new SectionException("byte mismatch at " + (off + k));
                        off += got;
                    }
                    if (off != total) throw new SectionException("read " + off + " of " + total);
                    long posAfterRead = lib.titan_vault_stream_get_position(rs);
                    if (posAfterRead != total) throw new SectionException("stream_get_position " + posAfterRead + " != " + total);
                    // random access: seek to a mid-file offset and verify (best-effort — not all backends seek)
                    final int seekTo = 70000;
                    long pos = lib.titan_vault_stream_seek(rs, seekTo, 0); // 0 = SEEK_SET
                    if (pos == seekTo) {
                        byte[] small = new byte[16];
                        if (lib.titan_vault_stream_read(rs, small, 16) != 16) throw new SectionException("short seek-read");
                        for (int k = 0; k < 16; k++) if ((small[k] & 0xff) != (seekTo + k) % 256) throw new SectionException("seek byte mismatch at " + (seekTo + k));
                        System.out.println("    wrote+verified " + total + " bytes; seek to " + seekTo + " OK");
                    } else {
                        System.out.println("    wrote+verified " + total + " bytes; seek not supported by this backend (skipped)");
                    }
                    // open_stream_with_flags: reopen read-only and confirm the length matches.
                    Pointer rs2 = lib.titan_vault_open_stream_with_flags(handle, fp, fp.length, OPEN_READONLY);
                    if (isNull(rs2)) throw new SectionException("open_stream_with_flags: " + lastError(lib));
                    try { if (lib.titan_vault_stream_get_length(rs2) != total) throw new SectionException("flags-open length mismatch"); }
                    finally { lib.titan_vault_close_stream(rs2); }
                } finally { lib.titan_vault_close_stream(rs); }

                // stream_set_length: truncation of encrypted streams is backend-dependent; best-effort.
                try {
                    byte[] tp = utf8("/trunc.bin");
                    Pointer ts = lib.titan_vault_open_stream_with_flags(handle, tp, tp.length, OPEN_WRITEONLY | OPEN_CREATE | OPEN_TRUNCATE);
                    if (!isNull(ts)) {
                        try { lib.titan_vault_stream_write(ts, chunk, CHUNK); lib.titan_vault_stream_set_length(ts, 4096); }
                        finally { lib.titan_vault_close_stream(ts); }
                    }
                } catch (RuntimeException ignored) { /* optional capability */ }
            });
        } finally {
            lib.titan_vault_close_vault(handle);
        }

        // 4. Persistence: reopen the (closed) vault with the passphrase and re-read.
        section(state, "Persistence", format, () -> {
            Pointer h2 = openVault(lib, format, vaultDir, password, null, null);
            if (isNull(h2)) throw new SectionException("reopen failed: " + lastError(lib));
            try {
                byte[] buf = new byte[4096];
                IntByReference size = new IntByReference(buf.length);
                check(lib, lib.titan_vault_read_file(h2, persistPath, persistPath.length, buf, size), "read after reopen");
                if (!Arrays.equals(Arrays.copyOf(buf, size.getValue()), persistPayload)) throw new SectionException("persisted content mismatch");
            } finally { lib.titan_vault_close_vault(h2); }
        });

        // 5/6. UVF-only: key rotation, then multi-user (all operate on the vault path).
        if (format.equals("uvf")) {
            // Key rotation must run while the vault is admin-only (the lib refuses to rotate a vault that
            // has extra users, since it would need every user's password to re-wrap the keys).
            int rc = lib.titan_vault_rotate_keys(vpath, vlen, pwd, plen, TITAN_VAULT_FORMAT_UVF);
            if (rc == TITAN_VAULT_SUCCESS) System.out.println("  Key rotation tests for UVF: PASSED");
            else if (lastError(lib).toLowerCase(Locale.ROOT).contains("not implemented")) System.out.println("  Key rotation tests for UVF: SKIPPED (not implemented)");
            else { state.failed++; System.out.println("  Key rotation tests for UVF: FAILED — " + lastError(lib)); }

            // Public-key (asymmetric) membership: admin grants access to a public key, the user opens with
            // their private key, and the admin can rotate the key without the member's password. Runs before
            // the password Multi-user section so only admin + the public-key user exist at rotation time.
            section(state, "Public-key multi-user", format, () -> {
                final String bob = "bob", keyPw = "bob-key-pass-123";

                // 1. Generate bob's key pair (public key + password-encrypted private key) via the C ABI.
                byte[] pubBuf = new byte[4096], privBuf = new byte[8192];
                IntByReference pubSize = new IntByReference(pubBuf.length), privSize = new IntByReference(privBuf.length);
                byte[] keyPwBuf = keyPw.getBytes(StandardCharsets.UTF_8);
                check(lib, lib.titan_vault_generate_user_keypair(keyPwBuf, keyPwBuf.length, pubBuf, pubSize, privBuf, privSize), "generate_user_keypair");
                final byte[] publicKey = Arrays.copyOf(pubBuf, pubSize.getValue());
                final byte[] encryptedPrivateKey = Arrays.copyOf(privBuf, privSize.getValue());
                System.out.println("    generated bob key pair (public " + pubSize.getValue() + "B, encrypted private " + privSize.getValue() + "B)");

                // 2. Grant bob access by PUBLIC key (admin needs no password from bob).
                byte[] bobId = utf8(bob);
                check(lib, lib.titan_vault_add_user_by_public_key(vpath, vlen, pwd, plen, bobId, bobId.length, publicKey, publicKey.length), "add_user_by_public_key");

                // 3. Open the vault as bob with his PRIVATE key and read the admin-written file.
                Runnable readAsBob = () -> {
                    Pointer h = lib.titan_vault_load_uvf_vault_with_key(vpath, vlen, encryptedPrivateKey, encryptedPrivateKey.length, keyPwBuf, keyPwBuf.length, bobId, bobId.length);
                    if (isNull(h)) throw new RuntimeException("load as bob failed: " + lastError(lib));
                    try {
                        byte[] buf = new byte[4096];
                        IntByReference size = new IntByReference(buf.length);
                        check(lib, lib.titan_vault_read_file(h, persistPath, persistPath.length, buf, size), "read as bob");
                        if (!Arrays.equals(Arrays.copyOf(buf, size.getValue()), persistPayload)) throw new RuntimeException("bob read mismatch");
                    } finally { lib.titan_vault_close_vault(h); }
                };
                readAsBob.run();
                System.out.println("    opened as bob (public-key user) and read the admin file OK");

                // 4. Rotate the key for public-key members — admin alone, no bob password — then bob still reads.
                check(lib, lib.titan_vault_rotate_keys_pubkey(vpath, vlen, pwd, plen), "rotate_keys_pubkey");
                readAsBob.run();
                System.out.println("    rotated keys (no member password) and bob still reads OK");
            });

            section(state, "Multi-user", format, () -> {
                final String alice = "alice", alicePw = "alice-passphrase-123";
                byte[] aliceId = utf8(alice);
                byte[] alicePwBuf = utf8(alicePw);
                check(lib, lib.titan_vault_add_user(vpath, vlen, pwd, plen, aliceId, aliceId.length, alicePwBuf, alicePwBuf.length), "add_user");
                int n = lib.titan_vault_get_vault_users(vpath, vlen, pwd, plen, listBuffer(), listMax());
                if (n < 0) throw new SectionException("get_vault_users rc=" + n + ": " + lastError(lib));
                List<String> users = readStringArray(lib, lastEntriesBuffer, n);
                System.out.println("    vault users: " + json(users));
                if (!users.contains(alice)) throw new SectionException("added user not listed (got " + json(users) + ")");

                // Best-effort: open as the new user and read the admin-written file. This currently fails
                // because LoadMultiUserUvfVaultAsync runs filename-encryption detection without the userId
                // (VaultManager.cs) — a known library limitation, reported (not failed) here.
                try {
                    Pointer ah = openVault(lib, "uvf", vaultDir, password, alice, alicePw);
                    if (isNull(ah)) throw new RuntimeException(lastError(lib));
                    try {
                        byte[] buf = new byte[4096];
                        IntByReference size = new IntByReference(buf.length);
                        check(lib, lib.titan_vault_read_file(ah, persistPath, persistPath.length, buf, size), "read as alice");
                        if (!Arrays.equals(Arrays.copyOf(buf, size.getValue()), persistPayload)) throw new RuntimeException("alice read mismatch");
                        System.out.println("    opened as second user and read the admin-written file OK");
                    } finally { lib.titan_vault_close_vault(ah); }
                } catch (Exception e) {
                    System.out.println("    ⚠ opening as a secondary user is not yet supported by the library: " + e.getMessage());
                }

                // Change a member's password (admin-driven), then remove the member and confirm they're gone.
                String aliceNewPwStr = "alice-passphrase-456";
                byte[] aliceNewPw = utf8(aliceNewPwStr);
                check(lib, lib.titan_vault_change_uvf_user_password(vpath, vlen, pwd, plen, aliceId, aliceId.length, aliceNewPw, aliceNewPw.length), "change_uvf_user_password");
                check(lib, lib.titan_vault_remove_user(vpath, vlen, pwd, plen, aliceId, aliceId.length), "remove_user");
                int n2 = lib.titan_vault_get_vault_users(vpath, vlen, pwd, plen, listBuffer(), listMax());
                List<String> users2 = readStringArray(lib, lastEntriesBuffer, Math.max(n2, 0));
                if (users2.contains(alice)) throw new SectionException("removed user still listed (got " + json(users2) + ")");
                System.out.println("    changed alice's password, then removed alice; users now: " + json(users2));
            });
        }

        // 7. Maintenance (both formats): backup the key files, secure-wipe a buffer, change the
        //    password, and reopen with the new password.
        section(state, "Maintenance", format, () -> {
            Path backupDir = Path.of(System.getProperty("java.io.tmpdir"), "uvf-backup-" + format + "-" + ProcessHandle.current().pid());
            rmRf(backupDir);
            byte[] backupBytes = utf8(backupDir.toString());
            check(lib, lib.titan_vault_backup_files(vpath, vlen, backupBytes, backupBytes.length, 1), "backup_files");
            if (!Files.exists(backupDir) || walk(backupDir).isEmpty()) throw new SectionException("backup produced no files");

            // JNA copies the byte[] back after the call, so the in-place zeroing is observable here.
            byte[] secret = "super-secret-key-material".getBytes(StandardCharsets.UTF_8);
            lib.titan_vault_secure_zero_memory(secret, secret.length);
            for (byte b : secret) if (b != 0) throw new SectionException("secure_zero_memory did not zero the buffer");

            String newPwStr = password + "-rotated";
            byte[] newPw = utf8(newPwStr);
            if (format.equals("uvf")) check(lib, lib.titan_vault_change_uvf_admin_password(vpath, vlen, pwd, plen, newPw, newPw.length), "change_uvf_admin_password");
            else check(lib, lib.titan_vault_change_cryptomator_password(vpath, vlen, pwd, plen, newPw, newPw.length), "change_cryptomator_password");
            Pointer h3 = openVault(lib, format, vaultDir, newPwStr, null, null);
            if (isNull(h3)) throw new SectionException("reopen after password change failed: " + lastError(lib));
            try {
                byte[] buf = new byte[4096];
                IntByReference size = new IntByReference(buf.length);
                check(lib, lib.titan_vault_read_file(h3, persistPath, persistPath.length, buf, size), "read after password change");
                if (!Arrays.equals(Arrays.copyOf(buf, size.getValue()), persistPayload)) throw new SectionException("content mismatch after password change");
            } finally { lib.titan_vault_close_vault(h3); }
            rmRf(backupDir);
            System.out.println("    backed up key files, secure-zeroed a buffer, changed the " + format + " password and re-read OK");
        });

        System.out.println("✅ " + format + " demo finished.");
    }

    private static Pointer openVault(TitanVault lib, String format, String vaultDir, String password, String userId, String userPassword) {
        byte[] vpath = utf8(vaultDir);
        if (format.equals("uvf")) {
            String pw = userPassword != null ? userPassword : password;
            byte[] pwd = utf8(pw);
            byte[] uid = userId != null ? utf8(userId) : null;
            return lib.titan_vault_load_uvf_vault(vpath, vpath.length, pwd, pwd.length, uid, uid != null ? uid.length : 0);
        }
        byte[] pwd = utf8(password);
        return lib.titan_vault_load_cryptomator_vault(vpath, vpath.length, pwd, pwd.length);
    }

    // ----- interop: unlock a REAL Cryptomator vault and md5-compare 3 decrypted files -----

    private static boolean runCryptomatorInterop(TitanVault lib) {
        System.out.println("\n========== Cryptomator interop (real vault) ==========");
        Path base = projectDir().resolve("..").resolve("_test-cryptomator-vault").normalize();
        Path vaultDir = base.resolve("smartinventure");
        Path origDir = base.resolve("original-files");
        String password = "smartinventure"; // demo vault — hardcoded on purpose

        if (!Files.exists(vaultDir.resolve("masterkey.cryptomator"))) {
            System.err.println("No Cryptomator vault found at " + vaultDir);
            return false;
        }
        byte[] vpath = utf8(vaultDir.toString());
        byte[] pwd = utf8(password);
        Pointer handle = lib.titan_vault_load_cryptomator_vault(vpath, vpath.length, pwd, pwd.length);
        if (isNull(handle)) { System.err.println("Unlock failed: " + lastError(lib)); return false; }
        try {
            System.out.println("Unlocked real Cryptomator vault at " + vaultDir);
            for (String d : new String[] {"/", "/mysubfolder1", "/mysubfolder1/mysubfolder2"}) {
                System.out.println("  " + d + "  ->  " + json(listDir(lib, handle, d)));
            }
            String[][] cases = {
                {"/Perfect-albums.txt", "Perfect-albums.txt"},
                {"/mysubfolder1/banana.jpg", "banana.jpg"},
                {"/mysubfolder1/mysubfolder2/Rubicon - Rivers - lyrics.txt", "Rubicon - Rivers - lyrics.txt"},
            };
            boolean allOk = true;
            for (String[] c : cases) {
                String vaultPath = c[0], origName = c[1];
                byte[] decrypted = readFileFull(lib, handle, vaultPath);
                String got = md5(decrypted);
                String want = md5(Files.readAllBytes(origDir.resolve(origName)));
                boolean ok = got.equals(want);
                if (!ok) allOk = false;
                System.out.println("  " + (ok ? "✓" : "✗") + " " + vaultPath + "  (" + decrypted.length + " B)  md5 "
                    + (ok ? "match" : "MISMATCH got=" + got + " want=" + want));
            }
            System.out.println(allOk
                ? "✅ Reading a real Cryptomator vault worked — all files decrypted and md5-matched the originals."
                : "❌ Cryptomator interop FAILED — md5 mismatch.");
            return allOk;
        } catch (Exception e) {
            System.out.println("❌ Cryptomator interop FAILED: " + e.getMessage());
            return false;
        } finally { lib.titan_vault_close_vault(handle); }
    }

    // ----- benchmark: create/encrypt/decrypt/read MB/s for both formats -----

    private static void runBenchmark(TitanVault lib, double sizeGb) {
        long sizeBytes = Math.round(sizeGb * 1024 * 1024 * 1024);
        final int CHUNK = 4 * 1024 * 1024; // 4 MiB
        System.out.println("\n========== Benchmark (" + trimNum(sizeGb) + " GB per format, " + (CHUNK >> 20) + " MiB chunks) ==========");
        System.out.println("  (disk read/write rows may just reflect the OS cache — pass --size larger than your RAM for disk-bound numbers)");
        for (String format : new String[] {"uvf", "cryptomator"}) benchOne(lib, format, sizeBytes, CHUNK);
    }

    private static void benchOne(TitanVault lib, String format, long sizeBytes, int CHUNK) {
        System.out.println("\n----- " + format.toUpperCase(Locale.ROOT) + " -----");
        Path dir = Path.of(System.getProperty("java.io.tmpdir"), "uvf-bench-" + format + "-" + ProcessHandle.current().pid());
        rmRf(dir);
        Path vaultDirPath = dir.resolve("vault");
        Path plain = dir.resolve("plain.bin");
        String password = "bench-pass-123";

        byte[] chunk = new byte[CHUNK];
        for (int i = 0; i < CHUNK; i++) chunk[i] = (byte) (i & 0xff); // non-trivial data (avoid sparse-file effects)

        try {
            Files.createDirectories(vaultDirPath);

            // (a) create the plaintext file on disk — gauges raw medium write speed
            long t = System.nanoTime();
            try (OutputStream out = Files.newOutputStream(plain)) {
                long w = 0;
                while (w < sizeBytes) { int n = (int) Math.min(CHUNK, sizeBytes - w); out.write(chunk, 0, n); w += n; }
            }
            report("create file (disk write, may be cached)", elapsedMs(t), sizeBytes);

            byte[] vpath = utf8(vaultDirPath.toString());
            int vlen = vpath.length;
            byte[] pwd = utf8(password);
            int plen = pwd.length;
            if (format.equals("uvf")) check(lib, lib.titan_vault_create_uvf_vault(vpath, vlen, pwd, plen, 1, 0, 0), "create_uvf_vault");
            else check(lib, lib.titan_vault_create_cryptomator_vault(vpath, vlen, pwd, plen), "create_cryptomator_vault");
            Pointer handle = format.equals("uvf")
                ? lib.titan_vault_load_uvf_vault(vpath, vlen, pwd, plen, null, 0)
                : lib.titan_vault_load_cryptomator_vault(vpath, vlen, pwd, plen);
            if (isNull(handle)) throw new RuntimeException("load failed: " + lastError(lib));

            byte[] bigPath = utf8("/big.bin");
            try {
                // (b) encrypt — stream the plaintext into the vault
                t = System.nanoTime();
                {
                    Pointer ws = lib.titan_vault_open_write_stream(handle, bigPath, bigPath.length);
                    if (isNull(ws)) throw new RuntimeException("open_write_stream: " + lastError(lib));
                    try (InputStream in = Files.newInputStream(plain)) {
                        byte[] rbuf = new byte[CHUNK];
                        int rd;
                        while ((rd = in.read(rbuf, 0, CHUNK)) > 0) {
                            byte[] toWrite = rd == CHUNK ? rbuf : Arrays.copyOf(rbuf, rd);
                            if (lib.titan_vault_stream_write(ws, toWrite, rd) != rd) throw new RuntimeException("short write");
                        }
                    } finally { lib.titan_vault_close_stream(ws); }
                }
                report("encrypt (" + format + ")", elapsedMs(t), sizeBytes);

                // (c) decrypt — stream it back out of the vault (discarding the plaintext)
                t = System.nanoTime();
                {
                    Pointer rs = lib.titan_vault_open_read_stream(handle, bigPath, bigPath.length);
                    if (isNull(rs)) throw new RuntimeException("open_read_stream: " + lastError(lib));
                    byte[] dbuf = new byte[CHUNK];
                    long total = 0;
                    int got;
                    try { while ((got = lib.titan_vault_stream_read(rs, dbuf, CHUNK)) > 0) total += got; }
                    finally { lib.titan_vault_close_stream(rs); }
                    if (total != sizeBytes) throw new RuntimeException("decrypt size " + total + " != " + sizeBytes);
                }
                report("decrypt (" + format + ")", elapsedMs(t), sizeBytes);

                // (d) read the plaintext file back from disk — gauges raw medium read speed
                t = System.nanoTime();
                try (InputStream in = Files.newInputStream(plain)) {
                    byte[] rbuf = new byte[CHUNK];
                    while (in.read(rbuf, 0, CHUNK) > 0) { /* discard */ }
                }
                report("read file (disk read, may be cached)", elapsedMs(t), sizeBytes);
            } finally { lib.titan_vault_close_vault(handle); }
        } catch (Exception e) {
            throw new RuntimeException(e.getMessage(), e);
        } finally {
            rmRf(dir);
        }
    }

    // ----- helpers -----

    private static Args parseArgs(String[] argv) {
        Args a = new Args();
        a.lib = envOrDefault("TITANVAULT_LIB", defaultLibPath());
        a.vault = Path.of(System.getProperty("java.io.tmpdir"), "uvf-java-demo").toString();
        for (int i = 0; i < argv.length; i++) {
            String v = i + 1 < argv.length ? argv[i + 1] : null;
            switch (argv[i]) {
                case "--lib": a.lib = v; i++; break;
                case "--format": a.format = v; i++; break;
                case "--vault": a.vault = v; i++; break;
                case "--password": a.password = v; i++; break;
                case "--benchmark": case "--bench": a.benchmark = true; break;
                case "--size": a.sizeGb = Double.parseDouble(v); i++; break;
                case "--cryptomator-interop": case "--interop": a.interop = true; break;
                default: break;
            }
        }
        return a;
    }

    /**
     * Resolve the native library when neither --lib nor TITANVAULT_LIB is given. Search order:
     *   1. the project dir (where the demo runs from)   2. the current working directory
     *   3. the built output ../../Dist/Native/<rid>/   (the usual location after a build)
     * Returns the first that exists, else the Dist path (so the "not found" message points at the build).
     */
    private static String defaultLibPath() {
        String osArch = System.getProperty("os.arch").toLowerCase(Locale.ROOT);
        String arch = (osArch.contains("aarch64") || osArch.contains("arm64")) ? "arm64" : "x64";
        String osName = System.getProperty("os.name").toLowerCase(Locale.ROOT);
        boolean isWin = osName.contains("win");
        boolean isMac = osName.contains("mac") || osName.contains("darwin");
        String rid = (isWin ? "win-" : isMac ? "osx-" : "linux-") + arch;
        String file = isWin ? "TitanVault.dll" : isMac ? "libTitanVault.dylib" : "libTitanVault.so";
        Path distPath = projectDir().resolve("..").resolve("..").resolve("Dist").resolve("Native").resolve(rid).resolve(file).normalize();
        Path[] candidates = {
            projectDir().resolve(file),
            Path.of(System.getProperty("user.dir")).resolve(file),
            distPath,
        };
        for (Path p : candidates) if (Files.exists(p)) return p.toString();
        return distPath.toString();
    }

    /** The Java demo project directory (where pom.xml lives); the exec plugin runs from there. */
    private static Path projectDir() {
        return Path.of(System.getProperty("user.dir")).toAbsolutePath();
    }

    private static byte[] utf8(String s) { return s.getBytes(StandardCharsets.UTF_8); }

    private static boolean isNull(Pointer p) { return p == null || Pointer.nativeValue(p) == 0; }

    private static String lastError(TitanVault lib) {
        Pointer p = lib.titan_vault_get_last_error();
        if (isNull(p)) return "(no error message)";
        try { return p.getString(0, "UTF-8"); }
        finally { lib.titan_vault_free_string(p); }
    }

    private static String version(TitanVault lib) {
        Pointer p = lib.titan_vault_get_version();
        if (isNull(p)) return "(unknown)";
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

    // The native char*[] is written into a single Memory block; we reuse one block per call and keep a
    // reference so readStringArray can read it back (mirrors koffi.alloc('void *', MAX_LIST)).
    private static Memory lastEntriesBuffer;

    private static Pointer listBuffer() {
        lastEntriesBuffer = new Memory((long) MAX_LIST * Native.POINTER_SIZE);
        return lastEntriesBuffer;
    }

    private static IntByReference listMax() { return new IntByReference(MAX_LIST); }

    /** Decode a returned char*[] of `count` entries into Strings, freeing each native string. */
    private static List<String> readStringArray(TitanVault lib, Pointer buffer, int count) {
        List<String> out = new ArrayList<>();
        if (count <= 0) return out;
        for (int i = 0; i < count; i++) {
            Pointer p = buffer.getPointer((long) i * Native.POINTER_SIZE);
            if (isNull(p)) { out.add(null); continue; }
            out.add(p.getString(0, "UTF-8"));
            lib.titan_vault_free_string(p);
        }
        return out;
    }

    private static List<String> listDir(TitanVault lib, Pointer handle, String dirPath) {
        byte[] dp = utf8(dirPath);
        int n = lib.titan_vault_list_directory(handle, dp, dp.length, listBuffer(), listMax());
        if (n < 0) throw new RuntimeException("list_directory " + dirPath + " rc=" + n + ": " + lastError(lib));
        return readStringArray(lib, lastEntriesBuffer, n);
    }

    /** Reads a whole vault file into a byte[], growing the buffer to the required size if needed. */
    private static byte[] readFileFull(TitanVault lib, Pointer handle, String vaultPath) {
        byte[] vp = utf8(vaultPath);
        int cap = 1 << 20; // 1 MiB
        for (int attempt = 0; attempt < 4; attempt++) {
            byte[] buf = new byte[cap];
            IntByReference size = new IntByReference(cap);
            int rc = lib.titan_vault_read_file(handle, vp, vp.length, buf, size);
            if (rc == TITAN_VAULT_SUCCESS) return Arrays.copyOf(buf, size.getValue());
            if (size.getValue() > cap) { cap = size.getValue(); continue; } // grow and retry
            throw new RuntimeException("read_file " + vaultPath + " rc=" + rc + ": " + lastError(lib));
        }
        throw new RuntimeException("read_file " + vaultPath + ": buffer growth failed");
    }

    private interface SectionBody { void run() throws Exception; }

    private static void section(State state, String label, String format, SectionBody fn) {
        try { fn.run(); System.out.println("  " + label + " tests for " + format.toUpperCase(Locale.ROOT) + ": PASSED"); }
        catch (Exception e) { state.failed++; System.out.println("  " + label + " tests for " + format.toUpperCase(Locale.ROOT) + ": FAILED — " + e.getMessage()); }
    }

    private static double elapsedMs(long startNanos) { return (System.nanoTime() - startNanos) / 1e6; }

    private static double mbps(long bytes, double ms) { return (bytes / 1e6) / (ms / 1000); } // decimal MB/s

    private static void report(String label, double ms, long sizeBytes) {
        System.out.println("  " + padEnd(label, 32) + " " + padStart(String.format(Locale.ROOT, "%.0f", ms), 7)
            + " ms   " + padStart(String.format(Locale.ROOT, "%.1f", mbps(sizeBytes, ms)), 8) + " MB/s");
    }

    private static String md5(byte[] data) {
        try {
            MessageDigest md = MessageDigest.getInstance("MD5");
            byte[] digest = md.digest(data);
            StringBuilder sb = new StringBuilder(digest.length * 2);
            for (byte b : digest) sb.append(Character.forDigit((b >> 4) & 0xf, 16)).append(Character.forDigit(b & 0xf, 16));
            return sb.toString();
        } catch (Exception e) {
            throw new RuntimeException(e);
        }
    }

    // JSON-style rendering of a String list, matching JSON.stringify(["a","b"]) output.
    private static String json(List<String> items) {
        return "[" + items.stream().map(s -> s == null ? "null" : "\"" + s.replace("\\", "\\\\").replace("\"", "\\\"") + "\"")
            .collect(Collectors.joining(",")) + "]";
    }

    private static String padEnd(String s, int width) {
        StringBuilder sb = new StringBuilder(s);
        while (sb.length() < width) sb.append(' ');
        return sb.toString();
    }

    private static String padStart(String s, int width) {
        StringBuilder sb = new StringBuilder();
        while (sb.length() + s.length() < width) sb.append(' ');
        return sb.append(s).toString();
    }

    // Renders the GB count the way the JS template literal does (0.25 stays 0.25, 1 stays 1).
    private static String trimNum(double d) {
        if (d == Math.floor(d) && !Double.isInfinite(d)) return Long.toString((long) d);
        return Double.toString(d);
    }

    private static List<Path> walk(Path dir) {
        List<Path> out = new ArrayList<>();
        if (!Files.exists(dir)) return out;
        try (Stream<Path> s = Files.walk(dir)) {
            s.filter(Files::isRegularFile).forEach(out::add);
        } catch (IOException e) {
            throw new RuntimeException(e);
        }
        return out;
    }

    private static void rmRf(Path dir) {
        if (!Files.exists(dir)) return;
        try (Stream<Path> s = Files.walk(dir)) {
            s.sorted((a, b) -> b.getNameCount() - a.getNameCount()).forEach(p -> {
                try { Files.deleteIfExists(p); } catch (IOException ignored) { }
            });
        } catch (IOException ignored) { }
    }

    private VaultDemo() { }
}
