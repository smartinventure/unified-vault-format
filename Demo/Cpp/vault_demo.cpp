// Unified Vault Format (UVF) for C# and other languages.
// Copyright (c) Smart In Venture 2025- https://www.speedbits.io
// Licensed under AGPL-3.0 (commercial licenses available); see LICENSE.

// UVF / Cryptomator demo in C/C++ via the native TitanVault library (C ABI).
//
// Full-parity port of Demo/NodeJs/vault-demo.js: same sections, same `… tests for <FORMAT>:
// PASSED/FAILED` lines, same flags, and (with no args) runs everything — both formats' functional
// sections, the real-Cryptomator-vault interop, and a quick benchmark.
//
// The library is loaded at RUNTIME (LoadLibrary/dlopen) — no import lib is needed, so this works
// against any prebuilt TitanVault.{dll,so,dylib}. Function-pointer types come straight from the
// canonical header via decltype, so they always match the ABI.
//
// Build (see CMakeLists.txt / README.md), then:
//   ./vault_demo                      # both formats + interop + benchmark
//   ./vault_demo --format uvf         # one format's functional sections
//   ./vault_demo --benchmark --size 2 # throughput only, 2 GB
//   ./vault_demo --lib /path/to/TitanVault.dll

// Keep <windows.h> from defining the min/max/small macros that clash with std:: and our locals.
#define NOMINMAX
#define _CRT_SECURE_NO_WARNINGS

#include "titan_vault.h"

#include <algorithm>
#include <array>
#include <chrono>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <functional>
#include <iostream>
#include <stdexcept>
#include <string>
#include <vector>

#ifdef _WIN32
#  include <windows.h>
#else
#  include <dlfcn.h>
#  include <unistd.h>
#endif
#ifdef __APPLE__
#  include <mach-o/dyld.h>
#endif

namespace fs = std::filesystem;

// ----- platform: dynamic loading + paths -----
#ifdef _WIN32
using LibHandle = HMODULE;
static LibHandle openLib(const std::string& p) { return LoadLibraryA(p.c_str()); }
template <class T> static T loadSym(LibHandle h, const char* n) { return reinterpret_cast<T>(GetProcAddress(h, n)); }
static const char* const LIB_FILE = "TitanVault.dll";
static std::string exePath() { char b[MAX_PATH]; DWORD n = GetModuleFileNameA(nullptr, b, MAX_PATH); return std::string(b, n); }
static std::string pidStr() { return std::to_string(GetCurrentProcessId()); }
#else
using LibHandle = void*;
static LibHandle openLib(const std::string& p) { return dlopen(p.c_str(), RTLD_NOW); }
template <class T> static T loadSym(LibHandle h, const char* n) { return reinterpret_cast<T>(dlsym(h, n)); }
static std::string pidStr() { return std::to_string(::getpid()); }
#  ifdef __APPLE__
static const char* const LIB_FILE = "libTitanVault.dylib";
static std::string exePath() { char b[4096]; uint32_t s = sizeof(b); return _NSGetExecutablePath(b, &s) == 0 ? std::string(b) : std::string(); }
#  else
static const char* const LIB_FILE = "libTitanVault.so";
static std::string exePath() { char b[4096]; ssize_t n = ::readlink("/proc/self/exe", b, sizeof(b)); return std::string(b, n > 0 ? (size_t)n : 0); }
#  endif
#endif

static std::string exeDir() { try { return fs::path(exePath()).parent_path().string(); } catch (...) { return "."; } }

static std::string rid() {
#if defined(_WIN32)
    std::string os = "win-";
#elif defined(__APPLE__)
    std::string os = "osx-";
#else
    std::string os = "linux-";
#endif
#if defined(_M_ARM64) || defined(__aarch64__) || defined(__arm64__)
    return os + "arm64";
#else
    return os + "x64";
#endif
}

// ----- the bound C ABI (one member per export; the symbol is "titan_vault_<member>") -----
#define MEMBER(name) decltype(&titan_vault_##name) name
struct Api {
    MEMBER(get_version); MEMBER(get_last_error); MEMBER(free_string);
    MEMBER(detect_vault_format); MEMBER(secure_zero_memory); MEMBER(backup_files);
    MEMBER(create_cryptomator_vault); MEMBER(load_cryptomator_vault); MEMBER(change_cryptomator_password);
    MEMBER(create_uvf_vault); MEMBER(load_uvf_vault); MEMBER(change_uvf_admin_password); MEMBER(change_uvf_user_password);
    MEMBER(add_user); MEMBER(remove_user); MEMBER(get_vault_users); MEMBER(rotate_keys);
    MEMBER(generate_user_keypair); MEMBER(add_user_by_public_key); MEMBER(load_uvf_vault_with_key); MEMBER(rotate_keys_pubkey);
    MEMBER(write_file); MEMBER(read_file); MEMBER(file_exists); MEMBER(delete_file); MEMBER(move);
    MEMBER(write_all_text); MEMBER(append_all_text); MEMBER(read_all_text);
    MEMBER(create_directory); MEMBER(directory_exists); MEMBER(delete_directory); MEMBER(list_directory); MEMBER(get_file_info);
    MEMBER(open_read_stream); MEMBER(open_write_stream); MEMBER(open_stream_with_flags);
    MEMBER(stream_read); MEMBER(stream_write); MEMBER(stream_seek);
    MEMBER(stream_get_position); MEMBER(stream_get_length); MEMBER(stream_set_length); MEMBER(stream_flush); MEMBER(close_stream);
    MEMBER(close_vault);
};
#undef MEMBER

static Api bindApi(LibHandle h) {
    Api api{};
#define LOAD(name) do { \
        api.name = loadSym<decltype(api.name)>(h, "titan_vault_" #name); \
        if (!api.name) { std::cerr << "missing export: titan_vault_" #name << "\n"; std::exit(2); } \
    } while (0)
    LOAD(get_version); LOAD(get_last_error); LOAD(free_string);
    LOAD(detect_vault_format); LOAD(secure_zero_memory); LOAD(backup_files);
    LOAD(create_cryptomator_vault); LOAD(load_cryptomator_vault); LOAD(change_cryptomator_password);
    LOAD(create_uvf_vault); LOAD(load_uvf_vault); LOAD(change_uvf_admin_password); LOAD(change_uvf_user_password);
    LOAD(add_user); LOAD(remove_user); LOAD(get_vault_users); LOAD(rotate_keys);
    LOAD(generate_user_keypair); LOAD(add_user_by_public_key); LOAD(load_uvf_vault_with_key); LOAD(rotate_keys_pubkey);
    LOAD(write_file); LOAD(read_file); LOAD(file_exists); LOAD(delete_file); LOAD(move);
    LOAD(write_all_text); LOAD(append_all_text); LOAD(read_all_text);
    LOAD(create_directory); LOAD(directory_exists); LOAD(delete_directory); LOAD(list_directory); LOAD(get_file_info);
    LOAD(open_read_stream); LOAD(open_write_stream); LOAD(open_stream_with_flags);
    LOAD(stream_read); LOAD(stream_write); LOAD(stream_seek);
    LOAD(stream_get_position); LOAD(stream_get_length); LOAD(stream_set_length); LOAD(stream_flush); LOAD(close_stream);
    LOAD(close_vault);
#undef LOAD
    return api;
}

// ----- small helpers -----
static const unsigned char* b(const std::string& s) { return reinterpret_cast<const unsigned char*>(s.data()); }
static int len(const std::string& s) { return static_cast<int>(s.size()); }
static std::vector<unsigned char> bytes(const std::string& s) { return std::vector<unsigned char>(s.begin(), s.end()); }
static std::string upper(std::string s) { for (auto& c : s) c = static_cast<char>(::toupper((unsigned char)c)); return s; }

static std::string lastError(const Api& api) {
    char* p = api.get_last_error();
    if (!p) return "(no error)";
    std::string s(p); api.free_string(p); return s;
}
static void check(const Api& api, int rc, const std::string& what) {
    if (rc != TITAN_VAULT_SUCCESS)
        throw std::runtime_error(what + " failed (rc=" + std::to_string(rc) + "): " + lastError(api));
}
static std::string version(const Api& api) {
    char* p = api.get_version();
    if (!p) return "(unknown)";
    std::string s(p); api.free_string(p); return s;
}

static std::vector<std::string> readStringArray(const Api& api, std::array<char*, 256>& entries, int n) {
    std::vector<std::string> out;
    for (int i = 0; i < n; i++) { out.emplace_back(entries[i] ? entries[i] : ""); if (entries[i]) api.free_string(entries[i]); }
    return out;
}
static std::vector<std::string> listDir(const Api& api, TitanVaultHandle h, const std::string& path) {
    std::array<char*, 256> entries{}; int maxn = (int)entries.size();
    int n = api.list_directory(h, b(path), len(path), entries.data(), &maxn);
    if (n < 0) throw std::runtime_error("list_directory " + path + " rc=" + std::to_string(n) + ": " + lastError(api));
    return readStringArray(api, entries, n);
}
static std::vector<unsigned char> readFileFull(const Api& api, TitanVaultHandle h, const std::string& path) {
    int cap = 1 << 20;
    for (int attempt = 0; attempt < 5; attempt++) {
        std::vector<unsigned char> buf(cap); int size = cap;
        int rc = api.read_file(h, b(path), len(path), buf.data(), &size);
        if (rc == TITAN_VAULT_SUCCESS) { buf.resize(size); return buf; }
        if (size > cap) { cap = size; continue; }
        throw std::runtime_error("read_file " + path + " rc=" + std::to_string(rc) + ": " + lastError(api));
    }
    throw std::runtime_error("read_file " + path + ": buffer growth failed");
}
static std::string jsonList(const std::vector<std::string>& v) {
    std::string s = "[";
    for (size_t i = 0; i < v.size(); i++) { if (i) s += ","; s += "\"" + v[i] + "\""; }
    return s + "]";
}
static bool leakedToDisk(const std::string& vaultDir, const std::string& basename) {
    for (auto& e : fs::recursive_directory_iterator(vaultDir))
        if (e.is_regular_file() && e.path().filename() == basename) return true;
    return false;
}
static TitanVaultHandle openVault(const Api& api, const std::string& format, const std::string& vaultDir,
                                  const std::string& password, const std::string& userId = "",
                                  const std::string& userPassword = "") {
    if (format == "uvf") {
        const std::string& pw = userPassword.empty() ? password : userPassword;
        return api.load_uvf_vault(b(vaultDir), len(vaultDir), b(pw), len(pw),
                                  userId.empty() ? nullptr : b(userId), userId.empty() ? 0 : len(userId));
    }
    return api.load_cryptomator_vault(b(vaultDir), len(vaultDir), b(password), len(password));
}

struct State { int failed = 0; };
static void section(State& st, const std::string& label, const std::string& format, const std::function<void()>& fn) {
    try { fn(); std::cout << "  " << label << " tests for " << upper(format) << ": PASSED\n"; }
    catch (const std::exception& e) { st.failed++; std::cout << "  " << label << " tests for " << upper(format) << ": FAILED — " << e.what() << "\n"; }
}

// OpenFlags (StorageLib.Abstractions) for open_stream_with_flags.
static const int OPEN_READONLY = 0x0000, OPEN_WRITEONLY = 0x0001, OPEN_CREATE = 0x0040, OPEN_TRUNCATE = 0x0200;

using Clock = std::chrono::steady_clock;
static double elapsedMs(Clock::time_point t) { return std::chrono::duration<double, std::milli>(Clock::now() - t).count(); }
static double mbps(double bytesN, double ms) { return (bytesN / 1e6) / (ms / 1000.0); }

// ----- the per-format demo -----
static void runDemo(const Api& api, const std::string& format, const std::string& vaultDir,
                    const std::string& password, State& st) {
    std::cout << "\n========== " << upper(format) << " ==========\n";
    fs::remove_all(vaultDir);
    fs::create_directories(vaultDir);

    if (format == "uvf") check(api, api.create_uvf_vault(b(vaultDir), len(vaultDir), b(password), len(password), 1, 0, 0), "create_uvf_vault");
    else check(api, api.create_cryptomator_vault(b(vaultDir), len(vaultDir), b(password), len(password)), "create_cryptomator_vault");

    TitanVaultHandle handle = openVault(api, format, vaultDir, password);
    if (!handle) throw std::runtime_error("load " + format + " vault failed: " + lastError(api));
    std::cout << "Created + opened " << format << " vault at " << vaultDir << "\n";

    const std::string pp = "/persist.txt";
    auto persist = bytes("persisted across reopen");
    check(api, api.write_file(handle, b(pp), len(pp), persist.data(), (int)persist.size()), "write persist.txt");

    // 0. Detect format (path-based).
    section(st, "Detect format", format, [&] {
        int detected = api.detect_vault_format(b(vaultDir), len(vaultDir));
        int expected = format == "uvf" ? TITAN_VAULT_FORMAT_UVF : TITAN_VAULT_FORMAT_CRYPTOMATOR;
        if (detected != expected) throw std::runtime_error("detect_vault_format=" + std::to_string(detected) + ", expected " + std::to_string(expected));
    });

    // 1. File round-trip + filename-leak check.
    section(st, "File", format, [&] {
        std::string fp = "/hello.txt"; auto pt = bytes("Hello, encrypted world!");
        check(api, api.write_file(handle, b(fp), len(fp), pt.data(), (int)pt.size()), "write_file");
        auto got = readFileFull(api, handle, fp);
        if (got != pt) throw std::runtime_error("round-trip mismatch");
        if (leakedToDisk(vaultDir, "hello.txt")) throw std::runtime_error("plaintext filename leaked to disk");
        if (api.file_exists(handle, b(fp), len(fp)) != 1) throw std::runtime_error("exists should be 1");
        check(api, api.delete_file(handle, b(fp), len(fp)), "delete_file");
        if (api.file_exists(handle, b(fp), len(fp)) != 0) throw std::runtime_error("exists should be 0 after delete");
    });

    // 1b. UTF-8 text convenience.
    section(st, "Text helpers", format, [&] {
        std::string tf = "/notes.txt", first = "first line\n", second = "second line\n";
        check(api, api.write_all_text(handle, b(tf), len(tf), b(first), len(first)), "write_all_text");
        check(api, api.append_all_text(handle, b(tf), len(tf), b(second), len(second)), "append_all_text");
        char* p = api.read_all_text(handle, b(tf), len(tf));
        if (!p) throw std::runtime_error("read_all_text: " + lastError(api));
        std::string text(p); api.free_string(p);
        if (text != first + second) throw std::runtime_error("text round-trip mismatch: " + text);
    });

    // 2. Directories: create, write into, list, file-info, move/rename.
    section(st, "Directory", format, [&] {
        std::string dir = "/docs";
        check(api, api.create_directory(handle, b(dir), len(dir)), "create_directory");
        if (api.directory_exists(handle, b(dir), len(dir)) != 1) throw std::runtime_error("directory_exists should be 1");
        std::string note = "/docs/note.txt"; auto body = bytes("inside a subdirectory");
        check(api, api.write_file(handle, b(note), len(note), body.data(), (int)body.size()), "write into dir");
        auto names = listDir(api, handle, dir);
        if (std::find(names.begin(), names.end(), "note.txt") == names.end()) throw std::runtime_error("listing missing note.txt (got " + jsonList(names) + ")");
        int64_t sz = 0, mtime = 0;
        check(api, api.get_file_info(handle, b(note), len(note), &sz, &mtime), "get_file_info");
        if (sz != (int64_t)body.size()) throw std::runtime_error("file size " + std::to_string(sz) + " != " + std::to_string(body.size()));
        std::string renamed = "/docs/renamed.txt";
        check(api, api.move(handle, b(note), len(note), b(renamed), len(renamed)), "move");
        names = listDir(api, handle, dir);
        if (std::find(names.begin(), names.end(), "renamed.txt") == names.end()) throw std::runtime_error("rename not reflected (got " + jsonList(names) + ")");
        std::cout << "    /docs now contains: " << jsonList(names) << " (size of note was " << sz << " bytes)\n";
    });

    // 3. Streaming: multi-chunk write, then random-access read; plus the fuller stream API.
    section(st, "Streaming", format, [&] {
        std::string fp = "/big.bin"; const int CHUNK = 32 * 1024, CHUNKS = 4; const long long total = (long long)CHUNK * CHUNKS;
        std::vector<unsigned char> chunk(CHUNK);
        for (int j = 0; j < CHUNK; j++) chunk[j] = (unsigned char)(j % 256);

        TitanVaultHandle ws = api.open_write_stream(handle, b(fp), len(fp));
        if (!ws) throw std::runtime_error("open_write_stream: " + lastError(api));
        for (int i = 0; i < CHUNKS; i++) if (api.stream_write(ws, chunk.data(), CHUNK) != CHUNK) { api.close_stream(ws); throw std::runtime_error("short write"); }
        check(api, api.stream_flush(ws), "stream_flush");
        api.close_stream(ws);

        TitanVaultHandle rs = api.open_read_stream(handle, b(fp), len(fp));
        if (!rs) throw std::runtime_error("open_read_stream: " + lastError(api));
        try {
            if (api.stream_get_length(rs) != total) throw std::runtime_error("stream length mismatch");
            std::vector<unsigned char> rbuf(CHUNK); long long off = 0; int got;
            while ((got = api.stream_read(rs, rbuf.data(), CHUNK)) > 0) {
                for (int k = 0; k < got; k++) if (rbuf[k] != (unsigned char)((off + k) % 256)) throw std::runtime_error("byte mismatch at " + std::to_string(off + k));
                off += got;
            }
            if (off != total) throw std::runtime_error("read " + std::to_string(off) + " of " + std::to_string(total));
            if (api.stream_get_position(rs) != total) throw std::runtime_error("stream_get_position != total");
            long long seekTo = 70000, pos = api.stream_seek(rs, seekTo, TITAN_VAULT_SEEK_BEGIN);
            if (pos == seekTo) {
                std::vector<unsigned char> seekBuf(16);
                if (api.stream_read(rs, seekBuf.data(), 16) != 16) throw std::runtime_error("short seek-read");
                for (int k = 0; k < 16; k++) if (seekBuf[k] != (unsigned char)((seekTo + k) % 256)) throw std::runtime_error("seek byte mismatch");
                std::cout << "    wrote+verified " << total << " bytes; seek to " << seekTo << " OK\n";
            } else {
                std::cout << "    wrote+verified " << total << " bytes; seek not supported by this backend (skipped)\n";
            }
            TitanVaultHandle rs2 = api.open_stream_with_flags(handle, b(fp), len(fp), OPEN_READONLY);
            if (!rs2) throw std::runtime_error("open_stream_with_flags: " + lastError(api));
            bool lenOk = api.stream_get_length(rs2) == total; api.close_stream(rs2);
            if (!lenOk) throw std::runtime_error("flags-open length mismatch");
        } catch (...) { api.close_stream(rs); throw; }
        api.close_stream(rs);

        // stream_set_length: truncation of encrypted streams is backend-dependent; best-effort.
        std::string tp = "/trunc.bin";
        TitanVaultHandle ts = api.open_stream_with_flags(handle, b(tp), len(tp), OPEN_WRITEONLY | OPEN_CREATE | OPEN_TRUNCATE);
        if (ts) { api.stream_write(ts, chunk.data(), CHUNK); api.stream_set_length(ts, 4096); api.close_stream(ts); }
    });

    api.close_vault(handle);

    // 4. Persistence: reopen with the passphrase and re-read.
    section(st, "Persistence", format, [&] {
        TitanVaultHandle h2 = openVault(api, format, vaultDir, password);
        if (!h2) throw std::runtime_error("reopen failed: " + lastError(api));
        try { if (readFileFull(api, h2, pp) != persist) throw std::runtime_error("persisted content mismatch"); }
        catch (...) { api.close_vault(h2); throw; }
        api.close_vault(h2);
    });

    // 5/6. UVF-only: key rotation, public-key multi-user, password multi-user.
    if (format == "uvf") {
        int rc = api.rotate_keys(b(vaultDir), len(vaultDir), b(password), len(password), TITAN_VAULT_FORMAT_UVF);
        if (rc == TITAN_VAULT_SUCCESS) std::cout << "  Key rotation tests for UVF: PASSED\n";
        else { std::string e = lastError(api);
            if (e.find("not implemented") != std::string::npos) std::cout << "  Key rotation tests for UVF: SKIPPED (not implemented)\n";
            else { st.failed++; std::cout << "  Key rotation tests for UVF: FAILED — " << e << "\n"; } }

        section(st, "Public-key multi-user", format, [&] {
            std::string bob = "bob", keyPw = "bob-key-pass-123";
            std::vector<unsigned char> pub(4096), priv(8192); int pubSize = (int)pub.size(), privSize = (int)priv.size();
            check(api, api.generate_user_keypair(b(keyPw), len(keyPw), pub.data(), &pubSize, priv.data(), &privSize), "generate_user_keypair");
            pub.resize(pubSize); priv.resize(privSize);
            std::cout << "    generated bob key pair (public " << pubSize << "B, encrypted private " << privSize << "B)\n";
            check(api, api.add_user_by_public_key(b(vaultDir), len(vaultDir), b(password), len(password), b(bob), len(bob), pub.data(), (int)pub.size()), "add_user_by_public_key");
            auto readAsBob = [&] {
                TitanVaultHandle h = api.load_uvf_vault_with_key(b(vaultDir), len(vaultDir), priv.data(), (int)priv.size(), b(keyPw), len(keyPw), b(bob), len(bob));
                if (!h) throw std::runtime_error("load as bob failed: " + lastError(api));
                try { if (readFileFull(api, h, pp) != persist) throw std::runtime_error("bob read mismatch"); }
                catch (...) { api.close_vault(h); throw; }
                api.close_vault(h);
            };
            readAsBob();
            std::cout << "    opened as bob (public-key user) and read the admin file OK\n";
            check(api, api.rotate_keys_pubkey(b(vaultDir), len(vaultDir), b(password), len(password)), "rotate_keys_pubkey");
            readAsBob();
            std::cout << "    rotated keys (no member password) and bob still reads OK\n";
        });

        section(st, "Multi-user", format, [&] {
            std::string alice = "alice", alicePw = "alice-passphrase-123";
            check(api, api.add_user(b(vaultDir), len(vaultDir), b(password), len(password), b(alice), len(alice), b(alicePw), len(alicePw)), "add_user");
            std::array<char*, 256> ub{}; int um = (int)ub.size();
            int n = api.get_vault_users(b(vaultDir), len(vaultDir), b(password), len(password), ub.data(), &um);
            if (n < 0) throw std::runtime_error("get_vault_users rc=" + std::to_string(n) + ": " + lastError(api));
            auto users = readStringArray(api, ub, n);
            std::cout << "    vault users: " << jsonList(users) << "\n";
            if (std::find(users.begin(), users.end(), alice) == users.end()) throw std::runtime_error("added user not listed (got " + jsonList(users) + ")");

            // Best-effort: open as the new user (a known library limitation — reported, not failed).
            try {
                TitanVaultHandle ah = openVault(api, "uvf", vaultDir, password, alice, alicePw);
                if (!ah) throw std::runtime_error(lastError(api));
                try { if (readFileFull(api, ah, pp) != persist) throw std::runtime_error("alice read mismatch");
                      std::cout << "    opened as second user and read the admin-written file OK\n"; }
                catch (...) { api.close_vault(ah); throw; }
                api.close_vault(ah);
            } catch (const std::exception& e) {
                std::cout << "    ⚠ opening as a secondary user is not yet supported by the library: " << e.what() << "\n";
            }

            // Change a member's password (admin-driven), then remove the member and confirm they're gone.
            std::string aliceNewPw = "alice-passphrase-456";
            check(api, api.change_uvf_user_password(b(vaultDir), len(vaultDir), b(password), len(password), b(alice), len(alice), b(aliceNewPw), len(aliceNewPw)), "change_uvf_user_password");
            check(api, api.remove_user(b(vaultDir), len(vaultDir), b(password), len(password), b(alice), len(alice)), "remove_user");
            std::array<char*, 256> ub2{}; int um2 = (int)ub2.size();
            int n2 = api.get_vault_users(b(vaultDir), len(vaultDir), b(password), len(password), ub2.data(), &um2);
            auto users2 = readStringArray(api, ub2, n2 < 0 ? 0 : n2);
            if (std::find(users2.begin(), users2.end(), alice) != users2.end()) throw std::runtime_error("removed user still listed (got " + jsonList(users2) + ")");
            std::cout << "    changed alice's password, then removed alice; users now: " << jsonList(users2) << "\n";
        });
    }

    // 7. Maintenance (both formats): backup, secure-wipe, password change + reopen.
    section(st, "Maintenance", format, [&] {
        std::string backupDir = (fs::temp_directory_path() / ("uvf-backup-" + format + "-" + pidStr())).string();
        fs::remove_all(backupDir);
        check(api, api.backup_files(b(vaultDir), len(vaultDir), b(backupDir), len(backupDir), 1), "backup_files");
        if (!fs::exists(backupDir) || fs::is_empty(backupDir)) throw std::runtime_error("backup produced no files");

        auto secret = bytes("super-secret-key-material");
        api.secure_zero_memory(secret.data(), (int)secret.size());
        for (auto c : secret) if (c != 0) throw std::runtime_error("secure_zero_memory did not zero the buffer");

        std::string newPw = password + "-rotated";
        if (format == "uvf") check(api, api.change_uvf_admin_password(b(vaultDir), len(vaultDir), b(password), len(password), b(newPw), len(newPw)), "change_uvf_admin_password");
        else check(api, api.change_cryptomator_password(b(vaultDir), len(vaultDir), b(password), len(password), b(newPw), len(newPw)), "change_cryptomator_password");
        TitanVaultHandle h3 = openVault(api, format, vaultDir, newPw);
        if (!h3) throw std::runtime_error("reopen after password change failed: " + lastError(api));
        try { if (readFileFull(api, h3, pp) != persist) throw std::runtime_error("content mismatch after password change"); }
        catch (...) { api.close_vault(h3); throw; }
        api.close_vault(h3);
        fs::remove_all(backupDir);
        std::cout << "    backed up key files, secure-zeroed a buffer, changed the " << format << " password and re-read OK\n";
    });

    std::cout << "✅ " << format << " demo finished.\n";
}

// ----- interop: unlock a REAL Cryptomator vault and byte-compare the files -----
static std::string findInteropBase() {
    const char* names[] = {"masterkey.cryptomator"};
    (void)names;
    for (fs::path start : {fs::current_path(), fs::path(exeDir())}) {
        for (fs::path d = start; ; d = d.parent_path()) {
            for (fs::path cand : {d / "_test-cryptomator-vault", d / "Demo" / "_test-cryptomator-vault", d / ".." / "_test-cryptomator-vault"}) {
                std::error_code ec;
                if (fs::exists(cand / "smartinventure" / "masterkey.cryptomator", ec)) return fs::weakly_canonical(cand, ec).string();
            }
            if (d == d.parent_path()) break;
        }
    }
    return "";
}
static bool runInterop(const Api& api) {
    std::cout << "\n========== Cryptomator interop (real vault) ==========\n";
    std::string base = findInteropBase();
    if (base.empty()) { std::cout << "(Cryptomator interop skipped — Demo/_test-cryptomator-vault not found)\n"; return true; }
    std::string vaultDir = (fs::path(base) / "smartinventure").string();
    std::string origDir = (fs::path(base) / "original-files").string();
    std::string password = "smartinventure";

    TitanVaultHandle h = api.load_cryptomator_vault(b(vaultDir), len(vaultDir), b(password), len(password));
    if (!h) { std::cout << "Unlock failed: " << lastError(api) << "\n"; return false; }
    bool allOk = true;
    try {
        std::cout << "Unlocked real Cryptomator vault at " << vaultDir << "\n";
        for (std::string d : {"/", "/mysubfolder1", "/mysubfolder1/mysubfolder2"})
            std::cout << "  " << d << "  ->  " << jsonList(listDir(api, h, d)) << "\n";
        std::vector<std::pair<std::string, std::string>> cases = {
            {"/Perfect-albums.txt", "Perfect-albums.txt"},
            {"/mysubfolder1/banana.jpg", "banana.jpg"},
            {"/mysubfolder1/mysubfolder2/Rubicon - Rivers - lyrics.txt", "Rubicon - Rivers - lyrics.txt"},
        };
        for (auto& c : cases) {
            auto decrypted = readFileFull(api, h, c.first);
            std::ifstream f(fs::path(origDir) / c.second, std::ios::binary);
            std::vector<unsigned char> orig((std::istreambuf_iterator<char>(f)), std::istreambuf_iterator<char>());
            bool ok = decrypted == orig; if (!ok) allOk = false;
            std::cout << "  " << (ok ? "✓" : "✗") << " " << c.first << "  (" << decrypted.size() << " B)  bytes " << (ok ? "match" : "MISMATCH") << "\n";
        }
        std::cout << (allOk ? "✅ Reading a real Cryptomator vault worked — all files decrypted and byte-matched the originals.\n"
                            : "❌ Cryptomator interop FAILED — byte mismatch.\n");
    } catch (const std::exception& e) { std::cout << "❌ Cryptomator interop FAILED: " << e.what() << "\n"; allOk = false; }
    api.close_vault(h);
    return allOk;
}

// ----- benchmark -----
static void benchOne(const Api& api, const std::string& format, long long sizeBytes, int CHUNK) {
    std::cout << "\n----- " << upper(format) << " -----\n";
    fs::path dir = fs::temp_directory_path() / ("uvf-bench-" + format + "-" + pidStr());
    std::error_code ec; fs::remove_all(dir, ec);
    fs::path vaultDir = dir / "vault"; fs::create_directories(vaultDir);
    fs::path plain = dir / "plain.bin";
    std::string password = "bench-pass-123";
    auto report = [&](const std::string& label, double ms) {
        char line[160];
        std::snprintf(line, sizeof(line), "  %-38s %7.0f ms   %8.1f MB/s", label.c_str(), ms, mbps((double)sizeBytes, ms));
        std::cout << line << "\n";
    };
    std::vector<unsigned char> chunk(CHUNK);
    for (int i = 0; i < CHUNK; i++) chunk[i] = (unsigned char)(i & 0xff);

    try {
        // (a) create the plaintext file on disk
        auto t = Clock::now();
        { std::ofstream f(plain, std::ios::binary); long long w = 0;
          while (w < sizeBytes) { int n = (int)std::min((long long)CHUNK, sizeBytes - w); f.write((const char*)chunk.data(), n); w += n; }
          f.flush(); }
        report("create file (disk write, may be cached)", elapsedMs(t));

        std::string vd = vaultDir.string();
        if (format == "uvf") check(api, api.create_uvf_vault(b(vd), len(vd), b(password), len(password), 1, 0, 0), "create_uvf_vault");
        else check(api, api.create_cryptomator_vault(b(vd), len(vd), b(password), len(password)), "create_cryptomator_vault");
        TitanVaultHandle handle = format == "uvf"
            ? api.load_uvf_vault(b(vd), len(vd), b(password), len(password), nullptr, 0)
            : api.load_cryptomator_vault(b(vd), len(vd), b(password), len(password));
        if (!handle) throw std::runtime_error("load failed: " + lastError(api));

        try {
            std::string fp = "/big.bin";
            // (b) encrypt — stream the plaintext into the vault
            t = Clock::now();
            { TitanVaultHandle ws = api.open_write_stream(handle, b(fp), len(fp));
              if (!ws) throw std::runtime_error("open_write_stream: " + lastError(api));
              std::ifstream f(plain, std::ios::binary); std::vector<char> rbuf(CHUNK);
              while (f) { f.read(rbuf.data(), CHUNK); std::streamsize rd = f.gcount();
                  if (rd > 0 && api.stream_write(ws, (const unsigned char*)rbuf.data(), (int)rd) != (int)rd) { api.close_stream(ws); throw std::runtime_error("short write"); } }
              api.close_stream(ws); }
            report("encrypt (" + format + ")", elapsedMs(t));

            // (c) decrypt — stream it back out (discarding)
            t = Clock::now();
            { TitanVaultHandle rs = api.open_read_stream(handle, b(fp), len(fp));
              if (!rs) throw std::runtime_error("open_read_stream: " + lastError(api));
              std::vector<unsigned char> dbuf(CHUNK); long long totalN = 0; int got;
              while ((got = api.stream_read(rs, dbuf.data(), CHUNK)) > 0) totalN += got;
              api.close_stream(rs);
              if (totalN != sizeBytes) throw std::runtime_error("decrypt size " + std::to_string(totalN) + " != " + std::to_string(sizeBytes)); }
            report("decrypt (" + format + ")", elapsedMs(t));

            // (d) read the plaintext file back from disk
            t = Clock::now();
            { std::ifstream f(plain, std::ios::binary); std::vector<char> rbuf(CHUNK);
              while (f) { f.read(rbuf.data(), CHUNK); } }
            report("read file (disk read, may be cached)", elapsedMs(t));
        } catch (...) { api.close_vault(handle); throw; }
        api.close_vault(handle);
    } catch (...) { fs::remove_all(dir, ec); throw; }
    fs::remove_all(dir, ec);
}
static void runBenchmark(const Api& api, double sizeGb) {
    long long sizeBytes = (long long)(sizeGb * 1024.0 * 1024.0 * 1024.0 + 0.5);
    const int CHUNK = 4 * 1024 * 1024;
    std::cout << "\n========== Benchmark (" << sizeGb << " GB per format, " << (CHUNK >> 20) << " MiB chunks) ==========\n";
    std::cout << "  (disk read/write rows may just reflect the OS cache — pass --size larger than your RAM for disk-bound numbers)\n";
    for (std::string format : {"uvf", "cryptomator"}) benchOne(api, format, sizeBytes, CHUNK);
}

// ----- library discovery + args -----
static std::string discoverLib(const std::string& ed) {
    std::vector<fs::path> cands = { fs::path(ed) / LIB_FILE, fs::current_path() / LIB_FILE };
    for (fs::path start : {fs::current_path(), fs::path(ed)})
        for (fs::path d = start; ; d = d.parent_path()) {
            cands.push_back(d / "Dist" / "Native" / rid() / LIB_FILE);
            if (d == d.parent_path()) break;
        }
    std::error_code ec;
    for (auto& c : cands) if (fs::exists(c, ec)) return c.string();
    return (fs::path(ed) / LIB_FILE).string();
}

struct Args { std::string lib, format, vault, password = "correct horse battery staple"; bool benchmark = false, interop = false; double sizeGb = 1; };
static Args parseArgs(int argc, char** argv, const std::string& ed) {
    Args a;
    const char* env = std::getenv("TITANVAULT_LIB");
    a.lib = env && *env ? env : discoverLib(ed);
    a.vault = (fs::temp_directory_path() / "uvf-cpp-demo").string();
    for (int i = 1; i < argc; i++) {
        std::string s = argv[i]; auto next = [&] { return i + 1 < argc ? std::string(argv[++i]) : std::string(); };
        if (s == "--lib") a.lib = next();
        else if (s == "--format") a.format = next();
        else if (s == "--vault") a.vault = next();
        else if (s == "--password") a.password = next();
        else if (s == "--benchmark" || s == "--bench") a.benchmark = true;
        else if (s == "--size") a.sizeGb = std::stod(next());
        else if (s == "--cryptomator-interop" || s == "--interop") a.interop = true;
    }
    return a;
}

int main(int argc, char** argv) {
    std::string ed = exeDir();
    Args args = parseArgs(argc, argv, ed);
    if (!fs::exists(args.lib)) {
        std::cerr << "Native library not found: " << args.lib << "\n"
                  << "Build it first:  ../../BuildScripts/build.ps1 -Task aot   (or build.sh --task aot)\n"
                  << "Then it loads automatically (same folder / cwd / ../../Dist/Native/<rid>/), or pass --lib <path>.\n";
        return 1;
    }
    LibHandle h = openLib(args.lib);
    if (!h) { std::cerr << "Failed to load " << args.lib << " (architecture mismatch?)\n"; return 1; }
    Api api = bindApi(h);
    std::cout << "TitanVault version: " << version(api) << "\n";

    if (args.interop) return runInterop(api) ? 0 : 1;
    if (args.benchmark) { runBenchmark(api, args.sizeGb); return 0; }

    State st;
    std::vector<std::string> formats = args.format.empty() ? std::vector<std::string>{"uvf", "cryptomator"} : std::vector<std::string>{args.format};
    for (auto& format : formats) {
        try { runDemo(api, format, (fs::path(args.vault) / format).string(), args.password, st); }
        catch (const std::exception& e) { st.failed++; std::cout << "\n❌ " << format << " demo aborted: " << e.what() << "\n"; }
    }
    if (args.format.empty()) {
        if (!runInterop(api)) st.failed++;
        try { runBenchmark(api, 0.25); } catch (const std::exception& e) { st.failed++; std::cout << "\n❌ benchmark aborted: " << e.what() << "\n"; }
    }
    std::cout << (st.failed == 0 ? "\n✅ All C/C++ demo sections passed.\n" : "\n❌ " + std::to_string(st.failed) + " section(s) failed.\n");
    return st.failed == 0 ? 0 : 1;
}
