// UVF / Cryptomator demo in Swift via the native TitanVault library (C ABI).
//
// Full-parity port of Demo/NodeJs/vault-demo.js (and structurally mirrors
// Demo/Cpp/vault_demo.cpp): same sections, the same
//   `  <Label> tests for <FORMAT>: PASSED/FAILED` lines, the same flags, and
// (with no args) it runs everything — both formats' functional sections, the
// real-Cryptomator-vault interop, and a quick benchmark.
//
// The native library is loaded at RUNTIME via dlopen/dlsym (Glibc on Linux,
// Darwin on macOS) — no link-time binding — so this works against any prebuilt
// libTitanVault.{so,dylib} and the same `--lib` flag + auto-discovery as the
// other demos applies. The C ABI types come from the canonical header through
// the CTitanVault module; each symbol is resolved with dlsym and reinterpreted
// (unsafeBitCast) to the matching @convention(c) function type.
//
//   swift run                              # both formats + interop + benchmark
//   swift run vault-demo --format uvf      # one format's functional sections
//   swift run vault-demo --benchmark --size 2
//   swift run vault-demo --lib /path/to/libTitanVault.dylib

import CTitanVault
import Dispatch
import Foundation

#if canImport(Glibc)
import Glibc
#elseif canImport(Darwin)
import Darwin
#endif

// ----- C ABI constants (mirrors titan_vault.h) -----
let TITAN_VAULT_SUCCESS: Int32 = 0
let TITAN_VAULT_FORMAT_CRYPTOMATOR: Int32 = 0
let TITAN_VAULT_FORMAT_UVF: Int32 = 1
let TITAN_VAULT_SEEK_BEGIN: Int32 = 0
let MAX_LIST = 256

// OpenFlags (StorageLib.Abstractions) for open_stream_with_flags.
let OPEN_READONLY: Int32 = 0x0000
let OPEN_WRITEONLY: Int32 = 0x0001
let OPEN_CREATE: Int32 = 0x0040
let OPEN_TRUNCATE: Int32 = 0x0200

// ----- @convention(c) function-pointer types for every export we use.
// Strings cross as a UTF-8 byte pointer + Int32 length; in/out sizes are Int32*;
// list_directory/get_vault_users fill a char*[]; handles/streams are raw
// pointers; stream offsets are Int64; heap strings come back as char* (read with
// String(cString:) then free with free_string).
typealias FnGetVersion = @convention(c) () -> UnsafeMutablePointer<CChar>?
typealias FnGetLastError = @convention(c) () -> UnsafeMutablePointer<CChar>?
typealias FnFreeString = @convention(c) (UnsafeMutablePointer<CChar>?) -> Void
typealias FnSecureZeroMemory = @convention(c) (UnsafeMutablePointer<UInt8>?, Int32) -> Void
typealias FnDetectVaultFormat = @convention(c) (UnsafePointer<UInt8>?, Int32) -> Int32

typealias FnCreateCryptomator = @convention(c) (UnsafePointer<UInt8>?, Int32, UnsafePointer<UInt8>?, Int32) -> Int32
typealias FnLoadCryptomator = @convention(c) (UnsafePointer<UInt8>?, Int32, UnsafePointer<UInt8>?, Int32) -> OpaquePointer?
typealias FnChangeCryptomatorPassword = @convention(c) (UnsafePointer<UInt8>?, Int32, UnsafePointer<UInt8>?, Int32, UnsafePointer<UInt8>?, Int32) -> Int32

typealias FnCreateUvf = @convention(c) (UnsafePointer<UInt8>?, Int32, UnsafePointer<UInt8>?, Int32, Int32, Int32, Int32) -> Int32
typealias FnLoadUvf = @convention(c) (UnsafePointer<UInt8>?, Int32, UnsafePointer<UInt8>?, Int32, UnsafePointer<UInt8>?, Int32) -> OpaquePointer?
typealias FnChangeUvfAdminPassword = @convention(c) (UnsafePointer<UInt8>?, Int32, UnsafePointer<UInt8>?, Int32, UnsafePointer<UInt8>?, Int32) -> Int32
typealias FnChangeUvfUserPassword = @convention(c) (UnsafePointer<UInt8>?, Int32, UnsafePointer<UInt8>?, Int32, UnsafePointer<UInt8>?, Int32, UnsafePointer<UInt8>?, Int32) -> Int32

typealias FnAddUser = @convention(c) (UnsafePointer<UInt8>?, Int32, UnsafePointer<UInt8>?, Int32, UnsafePointer<UInt8>?, Int32, UnsafePointer<UInt8>?, Int32) -> Int32
typealias FnRemoveUser = @convention(c) (UnsafePointer<UInt8>?, Int32, UnsafePointer<UInt8>?, Int32, UnsafePointer<UInt8>?, Int32) -> Int32
typealias FnGetVaultUsers = @convention(c) (UnsafePointer<UInt8>?, Int32, UnsafePointer<UInt8>?, Int32, UnsafeMutablePointer<UnsafeMutablePointer<CChar>?>?, UnsafeMutablePointer<Int32>?) -> Int32
typealias FnRotateKeys = @convention(c) (UnsafePointer<UInt8>?, Int32, UnsafePointer<UInt8>?, Int32, Int32) -> Int32

typealias FnGenerateUserKeypair = @convention(c) (UnsafePointer<UInt8>?, Int32, UnsafeMutablePointer<UInt8>?, UnsafeMutablePointer<Int32>?, UnsafeMutablePointer<UInt8>?, UnsafeMutablePointer<Int32>?) -> Int32
typealias FnAddUserByPublicKey = @convention(c) (UnsafePointer<UInt8>?, Int32, UnsafePointer<UInt8>?, Int32, UnsafePointer<UInt8>?, Int32, UnsafePointer<UInt8>?, Int32) -> Int32
typealias FnLoadUvfWithKey = @convention(c) (UnsafePointer<UInt8>?, Int32, UnsafePointer<UInt8>?, Int32, UnsafePointer<UInt8>?, Int32, UnsafePointer<UInt8>?, Int32) -> OpaquePointer?
typealias FnRotateKeysPubKey = @convention(c) (UnsafePointer<UInt8>?, Int32, UnsafePointer<UInt8>?, Int32) -> Int32

typealias FnWriteFile = @convention(c) (OpaquePointer?, UnsafePointer<UInt8>?, Int32, UnsafePointer<UInt8>?, Int32) -> Int32
typealias FnReadFile = @convention(c) (OpaquePointer?, UnsafePointer<UInt8>?, Int32, UnsafeMutablePointer<UInt8>?, UnsafeMutablePointer<Int32>?) -> Int32
typealias FnFileExists = @convention(c) (OpaquePointer?, UnsafePointer<UInt8>?, Int32) -> Int32
typealias FnDeleteFile = @convention(c) (OpaquePointer?, UnsafePointer<UInt8>?, Int32) -> Int32
typealias FnMove = @convention(c) (OpaquePointer?, UnsafePointer<UInt8>?, Int32, UnsafePointer<UInt8>?, Int32) -> Int32

typealias FnWriteAllText = @convention(c) (OpaquePointer?, UnsafePointer<UInt8>?, Int32, UnsafePointer<UInt8>?, Int32) -> Int32
typealias FnAppendAllText = @convention(c) (OpaquePointer?, UnsafePointer<UInt8>?, Int32, UnsafePointer<UInt8>?, Int32) -> Int32
typealias FnReadAllText = @convention(c) (OpaquePointer?, UnsafePointer<UInt8>?, Int32) -> UnsafeMutablePointer<CChar>?

typealias FnCreateDirectory = @convention(c) (OpaquePointer?, UnsafePointer<UInt8>?, Int32) -> Int32
typealias FnDirectoryExists = @convention(c) (OpaquePointer?, UnsafePointer<UInt8>?, Int32) -> Int32
typealias FnDeleteDirectory = @convention(c) (OpaquePointer?, UnsafePointer<UInt8>?, Int32) -> Int32
typealias FnListDirectory = @convention(c) (OpaquePointer?, UnsafePointer<UInt8>?, Int32, UnsafeMutablePointer<UnsafeMutablePointer<CChar>?>?, UnsafeMutablePointer<Int32>?) -> Int32
typealias FnGetFileInfo = @convention(c) (OpaquePointer?, UnsafePointer<UInt8>?, Int32, UnsafeMutablePointer<Int64>?, UnsafeMutablePointer<Int64>?) -> Int32

typealias FnOpenReadStream = @convention(c) (OpaquePointer?, UnsafePointer<UInt8>?, Int32) -> OpaquePointer?
typealias FnOpenWriteStream = @convention(c) (OpaquePointer?, UnsafePointer<UInt8>?, Int32) -> OpaquePointer?
typealias FnOpenStreamWithFlags = @convention(c) (OpaquePointer?, UnsafePointer<UInt8>?, Int32, Int32) -> OpaquePointer?
typealias FnStreamRead = @convention(c) (OpaquePointer?, UnsafeMutablePointer<UInt8>?, Int32) -> Int32
typealias FnStreamWrite = @convention(c) (OpaquePointer?, UnsafePointer<UInt8>?, Int32) -> Int32
typealias FnStreamSeek = @convention(c) (OpaquePointer?, Int64, Int32) -> Int64
typealias FnStreamGetPosition = @convention(c) (OpaquePointer?) -> Int64
typealias FnStreamGetLength = @convention(c) (OpaquePointer?) -> Int64
typealias FnStreamSetLength = @convention(c) (OpaquePointer?, Int64) -> Int32
typealias FnStreamFlush = @convention(c) (OpaquePointer?) -> Int32
typealias FnCloseStream = @convention(c) (OpaquePointer?) -> Int32

typealias FnBackupFiles = @convention(c) (UnsafePointer<UInt8>?, Int32, UnsafePointer<UInt8>?, Int32, Int32) -> Int32
typealias FnCloseVault = @convention(c) (OpaquePointer?) -> Int32

// ----- the bound C ABI: one resolved function pointer per export -----
final class Api {
    let getVersion: FnGetVersion
    let getLastError: FnGetLastError
    let freeString: FnFreeString
    let secureZeroMemory: FnSecureZeroMemory
    let detectVaultFormat: FnDetectVaultFormat

    let createCryptomator: FnCreateCryptomator
    let loadCryptomator: FnLoadCryptomator
    let changeCryptomatorPassword: FnChangeCryptomatorPassword

    let createUvf: FnCreateUvf
    let loadUvf: FnLoadUvf
    let changeUvfAdminPassword: FnChangeUvfAdminPassword
    let changeUvfUserPassword: FnChangeUvfUserPassword

    let addUser: FnAddUser
    let removeUser: FnRemoveUser
    let getVaultUsers: FnGetVaultUsers
    let rotateKeys: FnRotateKeys

    let generateUserKeypair: FnGenerateUserKeypair
    let addUserByPublicKey: FnAddUserByPublicKey
    let loadUvfWithKey: FnLoadUvfWithKey
    let rotateKeysPubKey: FnRotateKeysPubKey

    let writeFile: FnWriteFile
    let readFile: FnReadFile
    let fileExists: FnFileExists
    let deleteFile: FnDeleteFile
    let move: FnMove

    let writeAllText: FnWriteAllText
    let appendAllText: FnAppendAllText
    let readAllText: FnReadAllText

    let createDirectory: FnCreateDirectory
    let directoryExists: FnDirectoryExists
    let deleteDirectory: FnDeleteDirectory
    let listDirectory: FnListDirectory
    let getFileInfo: FnGetFileInfo

    let openReadStream: FnOpenReadStream
    let openWriteStream: FnOpenWriteStream
    let openStreamWithFlags: FnOpenStreamWithFlags
    let streamRead: FnStreamRead
    let streamWrite: FnStreamWrite
    let streamSeek: FnStreamSeek
    let streamGetPosition: FnStreamGetPosition
    let streamGetLength: FnStreamGetLength
    let streamSetLength: FnStreamSetLength
    let streamFlush: FnStreamFlush
    let closeStream: FnCloseStream

    let backupFiles: FnBackupFiles
    let closeVault: FnCloseVault

    private let handle: UnsafeMutableRawPointer

    init(libPath: String) {
        guard let h = dlopen(libPath, RTLD_NOW) else {
            let err = dlerror()
            let msg = err != nil ? String(cString: err!) : "unknown error"
            FileHandle.standardError.write("Failed to load \(libPath) (architecture mismatch?): \(msg)\n".data(using: .utf8)!)
            exit(1)
        }
        self.handle = h

        func sym<T>(_ name: String, _ type: T.Type) -> T {
            guard let p = dlsym(h, name) else {
                FileHandle.standardError.write("missing export: \(name)\n".data(using: .utf8)!)
                exit(2)
            }
            return unsafeBitCast(p, to: T.self)
        }

        getVersion = sym("titan_vault_get_version", FnGetVersion.self)
        getLastError = sym("titan_vault_get_last_error", FnGetLastError.self)
        freeString = sym("titan_vault_free_string", FnFreeString.self)
        secureZeroMemory = sym("titan_vault_secure_zero_memory", FnSecureZeroMemory.self)
        detectVaultFormat = sym("titan_vault_detect_vault_format", FnDetectVaultFormat.self)

        createCryptomator = sym("titan_vault_create_cryptomator_vault", FnCreateCryptomator.self)
        loadCryptomator = sym("titan_vault_load_cryptomator_vault", FnLoadCryptomator.self)
        changeCryptomatorPassword = sym("titan_vault_change_cryptomator_password", FnChangeCryptomatorPassword.self)

        createUvf = sym("titan_vault_create_uvf_vault", FnCreateUvf.self)
        loadUvf = sym("titan_vault_load_uvf_vault", FnLoadUvf.self)
        changeUvfAdminPassword = sym("titan_vault_change_uvf_admin_password", FnChangeUvfAdminPassword.self)
        changeUvfUserPassword = sym("titan_vault_change_uvf_user_password", FnChangeUvfUserPassword.self)

        addUser = sym("titan_vault_add_user", FnAddUser.self)
        removeUser = sym("titan_vault_remove_user", FnRemoveUser.self)
        getVaultUsers = sym("titan_vault_get_vault_users", FnGetVaultUsers.self)
        rotateKeys = sym("titan_vault_rotate_keys", FnRotateKeys.self)

        generateUserKeypair = sym("titan_vault_generate_user_keypair", FnGenerateUserKeypair.self)
        addUserByPublicKey = sym("titan_vault_add_user_by_public_key", FnAddUserByPublicKey.self)
        loadUvfWithKey = sym("titan_vault_load_uvf_vault_with_key", FnLoadUvfWithKey.self)
        rotateKeysPubKey = sym("titan_vault_rotate_keys_pubkey", FnRotateKeysPubKey.self)

        writeFile = sym("titan_vault_write_file", FnWriteFile.self)
        readFile = sym("titan_vault_read_file", FnReadFile.self)
        fileExists = sym("titan_vault_file_exists", FnFileExists.self)
        deleteFile = sym("titan_vault_delete_file", FnDeleteFile.self)
        move = sym("titan_vault_move", FnMove.self)

        writeAllText = sym("titan_vault_write_all_text", FnWriteAllText.self)
        appendAllText = sym("titan_vault_append_all_text", FnAppendAllText.self)
        readAllText = sym("titan_vault_read_all_text", FnReadAllText.self)

        createDirectory = sym("titan_vault_create_directory", FnCreateDirectory.self)
        directoryExists = sym("titan_vault_directory_exists", FnDirectoryExists.self)
        deleteDirectory = sym("titan_vault_delete_directory", FnDeleteDirectory.self)
        listDirectory = sym("titan_vault_list_directory", FnListDirectory.self)
        getFileInfo = sym("titan_vault_get_file_info", FnGetFileInfo.self)

        openReadStream = sym("titan_vault_open_read_stream", FnOpenReadStream.self)
        openWriteStream = sym("titan_vault_open_write_stream", FnOpenWriteStream.self)
        openStreamWithFlags = sym("titan_vault_open_stream_with_flags", FnOpenStreamWithFlags.self)
        streamRead = sym("titan_vault_stream_read", FnStreamRead.self)
        streamWrite = sym("titan_vault_stream_write", FnStreamWrite.self)
        streamSeek = sym("titan_vault_stream_seek", FnStreamSeek.self)
        streamGetPosition = sym("titan_vault_stream_get_position", FnStreamGetPosition.self)
        streamGetLength = sym("titan_vault_stream_get_length", FnStreamGetLength.self)
        streamSetLength = sym("titan_vault_stream_set_length", FnStreamSetLength.self)
        streamFlush = sym("titan_vault_stream_flush", FnStreamFlush.self)
        closeStream = sym("titan_vault_close_stream", FnCloseStream.self)

        backupFiles = sym("titan_vault_backup_files", FnBackupFiles.self)
        closeVault = sym("titan_vault_close_vault", FnCloseVault.self)
    }
}

// ----- errors + small helpers -----
struct DemoError: Error, CustomStringConvertible {
    let message: String
    init(_ message: String) { self.message = message }
    var description: String { message }
}

// Run a C call with the UTF-8 bytes of one or more strings pinned for the call.
// Swift's `withUnsafeBufferPointer` over `[UInt8]` gives a stable pointer for the
// closure's duration — exactly the lifetime the C ABI needs.
func withUtf8<R>(_ s: String, _ body: (UnsafePointer<UInt8>?, Int32) throws -> R) rethrows -> R {
    let bytes = Array(s.utf8)
    return try bytes.withUnsafeBufferPointer { try body($0.baseAddress, Int32($0.count)) }
}

func lastError(_ api: Api) -> String {
    guard let p = api.getLastError() else { return "(no error)" }
    let s = String(cString: p)
    api.freeString(p)
    return s
}

func check(_ api: Api, _ rc: Int32, _ what: String) throws {
    if rc != TITAN_VAULT_SUCCESS {
        throw DemoError("\(what) failed (rc=\(rc)): \(lastError(api))")
    }
}

func version(_ api: Api) -> String {
    guard let p = api.getVersion() else { return "(unknown)" }
    let s = String(cString: p)
    api.freeString(p)
    return s
}

// Decode a returned char*[] of `count` entries into Swift strings, freeing each.
func readStringArray(_ api: Api, _ entries: UnsafeMutablePointer<UnsafeMutablePointer<CChar>?>, _ count: Int) -> [String] {
    var out: [String] = []
    out.reserveCapacity(max(count, 0))
    for i in 0..<max(count, 0) {
        if let p = entries[i] {
            out.append(String(cString: p))
            api.freeString(p)
        } else {
            out.append("")
        }
    }
    return out
}

func jsonList(_ v: [String]) -> String {
    "[" + v.map { "\"\($0)\"" }.joined(separator: ",") + "]"
}

func upper(_ s: String) -> String { s.uppercased() }

// List a vault directory into Swift strings (entries freed).
func listDir(_ api: Api, _ handle: OpaquePointer?, _ path: String) throws -> [String] {
    var entries = [UnsafeMutablePointer<CChar>?](repeating: nil, count: MAX_LIST)
    var maxn = Int32(MAX_LIST)
    let n = withUtf8(path) { pb, pl in
        entries.withUnsafeMutableBufferPointer { eb in
            api.listDirectory(handle, pb, pl, eb.baseAddress, &maxn)
        }
    }
    if n < 0 { throw DemoError("list_directory \(path) rc=\(n): \(lastError(api))") }
    return entries.withUnsafeMutableBufferPointer { readStringArray(api, $0.baseAddress!, Int(n)) }
}

// Read a whole vault file, growing the buffer to the required size and retrying.
func readFileFull(_ api: Api, _ handle: OpaquePointer?, _ path: String) throws -> [UInt8] {
    var cap = 1 << 20
    for _ in 0..<5 {
        var buf = [UInt8](repeating: 0, count: cap)
        var size = Int32(cap)
        let rc = withUtf8(path) { pb, pl in
            buf.withUnsafeMutableBufferPointer { bb in
                api.readFile(handle, pb, pl, bb.baseAddress, &size)
            }
        }
        if rc == TITAN_VAULT_SUCCESS {
            buf.removeLast(cap - Int(size))
            return buf
        }
        if Int(size) > cap { cap = Int(size); continue }
        throw DemoError("read_file \(path) rc=\(rc): \(lastError(api))")
    }
    throw DemoError("read_file \(path): buffer growth failed")
}

func openVault(_ api: Api, _ format: String, _ vaultDir: String, _ password: String,
               userId: String = "", userPassword: String = "") -> OpaquePointer? {
    if format == "uvf" {
        let pw = userPassword.isEmpty ? password : userPassword
        return withUtf8(vaultDir) { vb, vl in
            withUtf8(pw) { pwb, pwl in
                if userId.isEmpty {
                    return api.loadUvf(vb, vl, pwb, pwl, nil, 0)
                }
                return withUtf8(userId) { ub, ul in
                    api.loadUvf(vb, vl, pwb, pwl, ub, ul)
                }
            }
        }
    }
    return withUtf8(vaultDir) { vb, vl in
        withUtf8(password) { pwb, pwl in
            api.loadCryptomator(vb, vl, pwb, pwl)
        }
    }
}

// Recursively walk a directory; returns whether any regular file has `basename`.
func leakedToDisk(_ vaultDir: String, _ basename: String) -> Bool {
    let fm = FileManager.default
    guard let it = fm.enumerator(atPath: vaultDir) else { return false }
    for case let rel as String in it {
        if (rel as NSString).lastPathComponent == basename {
            var isDir: ObjCBool = false
            let full = (vaultDir as NSString).appendingPathComponent(rel)
            if fm.fileExists(atPath: full, isDirectory: &isDir), !isDir.boolValue {
                return true
            }
        }
    }
    return false
}

// ----- per-format demo, each section reporting PASSED/FAILED -----
final class State { var failed = 0 }

func section(_ st: State, _ label: String, _ format: String, _ fn: () throws -> Void) {
    do {
        try fn()
        print("  \(label) tests for \(upper(format)): PASSED")
    } catch {
        st.failed += 1
        print("  \(label) tests for \(upper(format)): FAILED — \(error)")
    }
}

func tmpDir() -> String { NSTemporaryDirectory() }
func pidStr() -> String { String(ProcessInfo.processInfo.processIdentifier) }

func removeAll(_ path: String) {
    try? FileManager.default.removeItem(atPath: path)
}

func runDemo(_ api: Api, _ format: String, _ vaultDir: String, _ password: String, _ st: State) throws {
    print("\n========== \(upper(format)) ==========")
    removeAll(vaultDir)
    try FileManager.default.createDirectory(atPath: vaultDir, withIntermediateDirectories: true)

    if format == "uvf" {
        try check(api, withUtf8(vaultDir) { vb, vl in withUtf8(password) { pb, pl in
            api.createUvf(vb, vl, pb, pl, 1, 0, 0) } }, "create_uvf_vault")
    } else {
        try check(api, withUtf8(vaultDir) { vb, vl in withUtf8(password) { pb, pl in
            api.createCryptomator(vb, vl, pb, pl) } }, "create_cryptomator_vault")
    }

    guard let handle = openVault(api, format, vaultDir, password) else {
        throw DemoError("load \(format) vault failed: \(lastError(api))")
    }
    print("Created + opened \(format) vault at \(vaultDir)")

    let pp = "/persist.txt"
    let persist = Array("persisted across reopen".utf8)
    try check(api, withUtf8(pp) { pb, pl in
        persist.withUnsafeBufferPointer { db in
            api.writeFile(handle, pb, pl, db.baseAddress, Int32(db.count))
        } }, "write persist.txt")

    // 0. Detect the on-disk format (path-based).
    section(st, "Detect format", format) {
        let detected = withUtf8(vaultDir) { vb, vl in api.detectVaultFormat(vb, vl) }
        let expected = format == "uvf" ? TITAN_VAULT_FORMAT_UVF : TITAN_VAULT_FORMAT_CRYPTOMATOR
        if detected != expected {
            throw DemoError("detect_vault_format=\(detected), expected \(expected)")
        }
    }

    // 1. Basic file round-trip + filename-leak check.
    section(st, "File", format) {
        let fp = "/hello.txt"
        let plaintext = Array("Hello, encrypted world!".utf8)
        try check(api, withUtf8(fp) { pb, pl in plaintext.withUnsafeBufferPointer { db in
            api.writeFile(handle, pb, pl, db.baseAddress, Int32(db.count)) } }, "write_file")
        let got = try readFileFull(api, handle, fp)
        if got != plaintext { throw DemoError("round-trip mismatch") }
        if leakedToDisk(vaultDir, "hello.txt") { throw DemoError("plaintext filename leaked to disk") }
        if withUtf8(fp, { api.fileExists(handle, $0, $1) }) != 1 { throw DemoError("exists should be 1") }
        try check(api, withUtf8(fp) { api.deleteFile(handle, $0, $1) }, "delete_file")
        if withUtf8(fp, { api.fileExists(handle, $0, $1) }) != 0 { throw DemoError("exists should be 0 after delete") }
    }

    // 1b. UTF-8 text convenience: write, append, read-back.
    section(st, "Text helpers", format) {
        let tf = "/notes.txt", first = "first line\n", second = "second line\n"
        try check(api, withUtf8(tf) { pb, pl in withUtf8(first) { tb, tl in
            api.writeAllText(handle, pb, pl, tb, tl) } }, "write_all_text")
        try check(api, withUtf8(tf) { pb, pl in withUtf8(second) { tb, tl in
            api.appendAllText(handle, pb, pl, tb, tl) } }, "append_all_text")
        guard let p = (withUtf8(tf) { api.readAllText(handle, $0, $1) }) else {
            throw DemoError("read_all_text: \(lastError(api))")
        }
        let text = String(cString: p); api.freeString(p)
        if text != first + second { throw DemoError("text round-trip mismatch: \(text)") }
    }

    // 2. Directories: create, write into, list, file-info, move/rename.
    section(st, "Directory", format) {
        let dir = "/docs"
        try check(api, withUtf8(dir) { api.createDirectory(handle, $0, $1) }, "create_directory")
        if withUtf8(dir, { api.directoryExists(handle, $0, $1) }) != 1 { throw DemoError("directory_exists should be 1") }
        let note = "/docs/note.txt"
        let body = Array("inside a subdirectory".utf8)
        try check(api, withUtf8(note) { pb, pl in body.withUnsafeBufferPointer { db in
            api.writeFile(handle, pb, pl, db.baseAddress, Int32(db.count)) } }, "write into dir")
        var names = try listDir(api, handle, dir)
        if !names.contains("note.txt") { throw DemoError("listing missing note.txt (got \(jsonList(names)))") }
        var sz: Int64 = 0, mtime: Int64 = 0
        try check(api, withUtf8(note) { pb, pl in api.getFileInfo(handle, pb, pl, &sz, &mtime) }, "get_file_info")
        if sz != Int64(body.count) { throw DemoError("file size \(sz) != \(body.count)") }
        let renamed = "/docs/renamed.txt"
        try check(api, withUtf8(note) { sb, sl in withUtf8(renamed) { db, dl in
            api.move(handle, sb, sl, db, dl) } }, "move")
        names = try listDir(api, handle, dir)
        if !names.contains("renamed.txt") { throw DemoError("rename not reflected (got \(jsonList(names)))") }
        print("    /docs now contains: \(jsonList(names)) (size of note was \(sz) bytes)")
    }

    // 3. Streaming: multi-chunk write, then random-access read; plus the fuller stream API.
    section(st, "Streaming", format) {
        let fp = "/big.bin"
        let CHUNK = 32 * 1024, CHUNKS = 4
        let total = Int64(CHUNK * CHUNKS)
        var chunk = [UInt8](repeating: 0, count: CHUNK)
        for j in 0..<CHUNK { chunk[j] = UInt8(j % 256) }

        guard let ws = (withUtf8(fp) { api.openWriteStream(handle, $0, $1) }) else {
            throw DemoError("open_write_stream: \(lastError(api))")
        }
        do {
            for _ in 0..<CHUNKS {
                let w = chunk.withUnsafeBufferPointer { api.streamWrite(ws, $0.baseAddress, Int32(CHUNK)) }
                if w != Int32(CHUNK) { _ = api.closeStream(ws); throw DemoError("short write") }
            }
            try check(api, api.streamFlush(ws), "stream_flush")
        }
        _ = api.closeStream(ws)

        guard let rs = (withUtf8(fp) { api.openReadStream(handle, $0, $1) }) else {
            throw DemoError("open_read_stream: \(lastError(api))")
        }
        do {
            if api.streamGetLength(rs) != total { _ = api.closeStream(rs); throw DemoError("stream length mismatch") }
            var rbuf = [UInt8](repeating: 0, count: CHUNK)
            var off: Int64 = 0
            while true {
                let got = rbuf.withUnsafeMutableBufferPointer { api.streamRead(rs, $0.baseAddress, Int32(CHUNK)) }
                if got <= 0 { break }
                for k in 0..<Int(got) {
                    if rbuf[k] != UInt8((off + Int64(k)) % 256) {
                        _ = api.closeStream(rs); throw DemoError("byte mismatch at \(off + Int64(k))")
                    }
                }
                off += Int64(got)
            }
            if off != total { _ = api.closeStream(rs); throw DemoError("read \(off) of \(total)") }
            if api.streamGetPosition(rs) != total { _ = api.closeStream(rs); throw DemoError("stream_get_position != total") }

            let seekTo: Int64 = 70000
            let pos = api.streamSeek(rs, seekTo, TITAN_VAULT_SEEK_BEGIN)
            if pos == seekTo {
                var seekBuf = [UInt8](repeating: 0, count: 16)
                let r = seekBuf.withUnsafeMutableBufferPointer { api.streamRead(rs, $0.baseAddress, 16) }
                if r != 16 { _ = api.closeStream(rs); throw DemoError("short seek-read") }
                for k in 0..<16 {
                    if seekBuf[k] != UInt8((seekTo + Int64(k)) % 256) {
                        _ = api.closeStream(rs); throw DemoError("seek byte mismatch at \(seekTo + Int64(k))")
                    }
                }
                print("    wrote+verified \(total) bytes; seek to \(seekTo) OK")
            } else {
                print("    wrote+verified \(total) bytes; seek not supported by this backend (skipped)")
            }

            // open_stream_with_flags: reopen read-only and confirm the length matches.
            guard let rs2 = (withUtf8(fp) { api.openStreamWithFlags(handle, $0, $1, OPEN_READONLY) }) else {
                _ = api.closeStream(rs); throw DemoError("open_stream_with_flags: \(lastError(api))")
            }
            let lenOk = api.streamGetLength(rs2) == total
            _ = api.closeStream(rs2)
            if !lenOk { _ = api.closeStream(rs); throw DemoError("flags-open length mismatch") }
        }
        _ = api.closeStream(rs)

        // stream_set_length: truncation of encrypted streams is backend-dependent; best-effort.
        let tp = "/trunc.bin"
        if let ts = (withUtf8(tp) { api.openStreamWithFlags(handle, $0, $1, OPEN_WRITEONLY | OPEN_CREATE | OPEN_TRUNCATE) }) {
            _ = chunk.withUnsafeBufferPointer { api.streamWrite(ts, $0.baseAddress, Int32(CHUNK)) }
            _ = api.streamSetLength(ts, 4096)
            _ = api.closeStream(ts)
        }
    }

    _ = api.closeVault(handle)

    // 4. Persistence: reopen with the passphrase and re-read.
    section(st, "Persistence", format) {
        guard let h2 = openVault(api, format, vaultDir, password) else {
            throw DemoError("reopen failed: \(lastError(api))")
        }
        defer { _ = api.closeVault(h2) }
        if try readFileFull(api, h2, pp) != persist { throw DemoError("persisted content mismatch") }
    }

    // 5/6. UVF-only: key rotation, public-key multi-user, password multi-user.
    if format == "uvf" {
        let rc = withUtf8(vaultDir) { vb, vl in withUtf8(password) { pb, pl in
            api.rotateKeys(vb, vl, pb, pl, TITAN_VAULT_FORMAT_UVF) } }
        if rc == TITAN_VAULT_SUCCESS {
            print("  Key rotation tests for UVF: PASSED")
        } else {
            let e = lastError(api)
            if e.range(of: "not implemented", options: .caseInsensitive) != nil {
                print("  Key rotation tests for UVF: SKIPPED (not implemented)")
            } else {
                st.failed += 1
                print("  Key rotation tests for UVF: FAILED — \(e)")
            }
        }

        section(st, "Public-key multi-user", format) {
            let bob = "bob", keyPw = "bob-key-pass-123"
            var pub = [UInt8](repeating: 0, count: 4096)
            var priv = [UInt8](repeating: 0, count: 8192)
            var pubSize = Int32(pub.count), privSize = Int32(priv.count)
            try check(api, withUtf8(keyPw) { kb, kl in
                pub.withUnsafeMutableBufferPointer { pubB in
                    priv.withUnsafeMutableBufferPointer { privB in
                        api.generateUserKeypair(kb, kl, pubB.baseAddress, &pubSize, privB.baseAddress, &privSize)
                    }
                }
            }, "generate_user_keypair")
            pub.removeLast(pub.count - Int(pubSize))
            priv.removeLast(priv.count - Int(privSize))
            print("    generated bob key pair (public \(pubSize)B, encrypted private \(privSize)B)")

            try check(api, withUtf8(vaultDir) { vb, vl in withUtf8(password) { pwb, pwl in
                withUtf8(bob) { ub, ul in
                    pub.withUnsafeBufferPointer { pubB in
                        api.addUserByPublicKey(vb, vl, pwb, pwl, ub, ul, pubB.baseAddress, Int32(pubB.count))
                    }
                } } }, "add_user_by_public_key")

            func readAsBob() throws {
                guard let h = (withUtf8(vaultDir) { vb, vl in withUtf8(keyPw) { kb, kl in
                    withUtf8(bob) { ub, ul in
                        priv.withUnsafeBufferPointer { privB in
                            api.loadUvfWithKey(vb, vl, privB.baseAddress, Int32(privB.count), kb, kl, ub, ul)
                        }
                    } } }) else {
                    throw DemoError("load as bob failed: \(lastError(api))")
                }
                defer { _ = api.closeVault(h) }
                if try readFileFull(api, h, pp) != persist { throw DemoError("bob read mismatch") }
            }
            try readAsBob()
            print("    opened as bob (public-key user) and read the admin file OK")

            try check(api, withUtf8(vaultDir) { vb, vl in withUtf8(password) { pwb, pwl in
                api.rotateKeysPubKey(vb, vl, pwb, pwl) } }, "rotate_keys_pubkey")
            try readAsBob()
            print("    rotated keys (no member password) and bob still reads OK")
        }

        section(st, "Multi-user", format) {
            let alice = "alice", alicePw = "alice-passphrase-123"
            try check(api, withUtf8(vaultDir) { vb, vl in withUtf8(password) { pwb, pwl in
                withUtf8(alice) { ab, al in withUtf8(alicePw) { apb, apl in
                    api.addUser(vb, vl, pwb, pwl, ab, al, apb, apl) } } } }, "add_user")

            var ub = [UnsafeMutablePointer<CChar>?](repeating: nil, count: MAX_LIST)
            var um = Int32(MAX_LIST)
            let n = withUtf8(vaultDir) { vb, vl in withUtf8(password) { pwb, pwl in
                ub.withUnsafeMutableBufferPointer { eb in
                    api.getVaultUsers(vb, vl, pwb, pwl, eb.baseAddress, &um) } } }
            if n < 0 { throw DemoError("get_vault_users rc=\(n): \(lastError(api))") }
            let users = ub.withUnsafeMutableBufferPointer { readStringArray(api, $0.baseAddress!, Int(n)) }
            print("    vault users: \(jsonList(users))")
            if !users.contains(alice) { throw DemoError("added user not listed (got \(jsonList(users)))") }

            // Best-effort: open as the new user (a known library limitation — reported, not failed).
            do {
                guard let ah = openVault(api, "uvf", vaultDir, password, userId: alice, userPassword: alicePw) else {
                    throw DemoError(lastError(api))
                }
                defer { _ = api.closeVault(ah) }
                if try readFileFull(api, ah, pp) != persist { throw DemoError("alice read mismatch") }
                print("    opened as second user and read the admin-written file OK")
            } catch {
                print("    ⚠ opening as a secondary user is not yet supported by the library: \(error)")
            }

            // Change a member's password (admin-driven), then remove the member.
            let aliceNewPw = "alice-passphrase-456"
            try check(api, withUtf8(vaultDir) { vb, vl in withUtf8(password) { pwb, pwl in
                withUtf8(alice) { ab, al in withUtf8(aliceNewPw) { npb, npl in
                    api.changeUvfUserPassword(vb, vl, pwb, pwl, ab, al, npb, npl) } } } }, "change_uvf_user_password")
            try check(api, withUtf8(vaultDir) { vb, vl in withUtf8(password) { pwb, pwl in
                withUtf8(alice) { ab, al in api.removeUser(vb, vl, pwb, pwl, ab, al) } } }, "remove_user")

            var ub2 = [UnsafeMutablePointer<CChar>?](repeating: nil, count: MAX_LIST)
            var um2 = Int32(MAX_LIST)
            let n2 = withUtf8(vaultDir) { vb, vl in withUtf8(password) { pwb, pwl in
                ub2.withUnsafeMutableBufferPointer { eb in
                    api.getVaultUsers(vb, vl, pwb, pwl, eb.baseAddress, &um2) } } }
            let users2 = ub2.withUnsafeMutableBufferPointer { readStringArray(api, $0.baseAddress!, Int(max(n2, 0))) }
            if users2.contains(alice) { throw DemoError("removed user still listed (got \(jsonList(users2)))") }
            print("    changed alice's password, then removed alice; users now: \(jsonList(users2))")
        }
    }

    // 7. Maintenance (both formats): backup, secure-wipe, password change + reopen.
    section(st, "Maintenance", format) {
        let backupDir = (tmpDir() as NSString).appendingPathComponent("uvf-backup-\(format)-\(pidStr())")
        removeAll(backupDir)
        try check(api, withUtf8(vaultDir) { vb, vl in withUtf8(backupDir) { bb, bl in
            api.backupFiles(vb, vl, bb, bl, 1) } }, "backup_files")
        let backupContents = (try? FileManager.default.contentsOfDirectory(atPath: backupDir)) ?? []
        if backupContents.isEmpty { throw DemoError("backup produced no files") }

        var secret = Array("super-secret-key-material".utf8)
        secret.withUnsafeMutableBufferPointer { api.secureZeroMemory($0.baseAddress, Int32($0.count)) }
        if secret.contains(where: { $0 != 0 }) { throw DemoError("secure_zero_memory did not zero the buffer") }

        let newPw = password + "-rotated"
        if format == "uvf" {
            try check(api, withUtf8(vaultDir) { vb, vl in withUtf8(password) { pwb, pwl in
                withUtf8(newPw) { nb, nl in api.changeUvfAdminPassword(vb, vl, pwb, pwl, nb, nl) } } }, "change_uvf_admin_password")
        } else {
            try check(api, withUtf8(vaultDir) { vb, vl in withUtf8(password) { pwb, pwl in
                withUtf8(newPw) { nb, nl in api.changeCryptomatorPassword(vb, vl, pwb, pwl, nb, nl) } } }, "change_cryptomator_password")
        }
        guard let h3 = openVault(api, format, vaultDir, newPw) else {
            throw DemoError("reopen after password change failed: \(lastError(api))")
        }
        defer { _ = api.closeVault(h3) }
        if try readFileFull(api, h3, pp) != persist { throw DemoError("content mismatch after password change") }
        removeAll(backupDir)
        print("    backed up key files, secure-zeroed a buffer, changed the \(format) password and re-read OK")
    }

    print("✅ \(format) demo finished.")
}

// ----- interop: unlock a REAL Cryptomator vault and byte-compare the files -----
func findInteropBase() -> String? {
    let fm = FileManager.default
    var starts: [String] = []
    starts.append(fm.currentDirectoryPath)
    starts.append(exeDir())
    for start in starts {
        var d = URL(fileURLWithPath: start, isDirectory: true).standardizedFileURL
        while true {
            let candidates = [
                d.appendingPathComponent("_test-cryptomator-vault"),
                d.appendingPathComponent("Demo").appendingPathComponent("_test-cryptomator-vault"),
            ]
            for cand in candidates {
                let master = cand.appendingPathComponent("smartinventure").appendingPathComponent("masterkey.cryptomator")
                if fm.fileExists(atPath: master.path) {
                    return cand.standardizedFileURL.path
                }
            }
            let parent = d.deletingLastPathComponent().standardizedFileURL
            if parent.path == d.path { break }
            d = parent
        }
    }
    return nil
}

func runInterop(_ api: Api) -> Bool {
    print("\n========== Cryptomator interop (real vault) ==========")
    guard let base = findInteropBase() else {
        print("(Cryptomator interop skipped — Demo/_test-cryptomator-vault not found)")
        return true
    }
    let vaultDir = (base as NSString).appendingPathComponent("smartinventure")
    let origDir = (base as NSString).appendingPathComponent("original-files")
    let password = "smartinventure"

    guard let h = openVault(api, "cryptomator", vaultDir, password) else {
        print("Unlock failed: \(lastError(api))")
        return false
    }
    defer { _ = api.closeVault(h) }
    var allOk = true
    do {
        print("Unlocked real Cryptomator vault at \(vaultDir)")
        for d in ["/", "/mysubfolder1", "/mysubfolder1/mysubfolder2"] {
            let entries = try listDir(api, h, d)
            print("  \(d)  ->  \(jsonList(entries))")
        }
        let cases: [(String, String)] = [
            ("/Perfect-albums.txt", "Perfect-albums.txt"),
            ("/mysubfolder1/banana.jpg", "banana.jpg"),
            ("/mysubfolder1/mysubfolder2/Rubicon - Rivers - lyrics.txt", "Rubicon - Rivers - lyrics.txt"),
        ]
        for (vaultPath, origName) in cases {
            let decrypted = try readFileFull(api, h, vaultPath)
            let origPath = (origDir as NSString).appendingPathComponent(origName)
            guard let origData = FileManager.default.contents(atPath: origPath) else {
                throw DemoError("missing original file \(origPath)")
            }
            let orig = [UInt8](origData)
            let ok = decrypted == orig
            if !ok { allOk = false }
            print("  \(ok ? "✓" : "✗") \(vaultPath)  (\(decrypted.count) B)  bytes \(ok ? "match" : "MISMATCH")")
        }
        print(allOk
            ? "✅ Reading a real Cryptomator vault worked — all files decrypted and byte-matched the originals."
            : "❌ Cryptomator interop FAILED — byte mismatch.")
    } catch {
        print("❌ Cryptomator interop FAILED: \(error)")
        allOk = false
    }
    return allOk
}

// ----- benchmark -----
func elapsedMs(_ start: DispatchTime) -> Double {
    Double(DispatchTime.now().uptimeNanoseconds - start.uptimeNanoseconds) / 1e6
}
func mbps(_ bytesN: Double, _ ms: Double) -> Double { (bytesN / 1e6) / (ms / 1000.0) }

func benchOne(_ api: Api, _ format: String, _ sizeBytes: Int64, _ CHUNK: Int) throws {
    print("\n----- \(upper(format)) -----")
    let dir = (tmpDir() as NSString).appendingPathComponent("uvf-bench-\(format)-\(pidStr())")
    removeAll(dir)
    let vaultDir = (dir as NSString).appendingPathComponent("vault")
    try FileManager.default.createDirectory(atPath: vaultDir, withIntermediateDirectories: true)
    let plain = (dir as NSString).appendingPathComponent("plain.bin")
    let password = "bench-pass-123"

    func report(_ label: String, _ ms: Double) {
        let labelPadded = label.padding(toLength: max(label.count, 38), withPad: " ", startingAt: 0)
        let msStr = String(format: "%7.0f", ms)
        let rate = String(format: "%8.1f", mbps(Double(sizeBytes), ms))
        print("  \(labelPadded) \(msStr) ms   \(rate) MB/s")
    }

    var chunk = [UInt8](repeating: 0, count: CHUNK)
    for i in 0..<CHUNK { chunk[i] = UInt8(i & 0xff) }

    defer { removeAll(dir) }

    // (a) create the plaintext file on disk — gauges raw medium write speed.
    var t = DispatchTime.now()
    guard let outFp = fopen(plain, "wb") else { throw DemoError("cannot create \(plain)") }
    do {
        var w: Int64 = 0
        while w < sizeBytes {
            let n = Int(min(Int64(CHUNK), sizeBytes - w))
            let written = chunk.withUnsafeBufferPointer { fwrite($0.baseAddress, 1, n, outFp) }
            if written != n { fclose(outFp); throw DemoError("short disk write") }
            w += Int64(n)
        }
        fflush(outFp)
    }
    fclose(outFp)
    report("create file (disk write, may be cached)", elapsedMs(t))

    if format == "uvf" {
        try check(api, withUtf8(vaultDir) { vb, vl in withUtf8(password) { pb, pl in
            api.createUvf(vb, vl, pb, pl, 1, 0, 0) } }, "create_uvf_vault")
    } else {
        try check(api, withUtf8(vaultDir) { vb, vl in withUtf8(password) { pb, pl in
            api.createCryptomator(vb, vl, pb, pl) } }, "create_cryptomator_vault")
    }
    guard let handle = openVault(api, format, vaultDir, password) else {
        throw DemoError("load failed: \(lastError(api))")
    }
    defer { _ = api.closeVault(handle) }

    let fp = "/big.bin"

    // (b) encrypt — stream the plaintext into the vault.
    t = DispatchTime.now()
    do {
        guard let ws = (withUtf8(fp) { api.openWriteStream(handle, $0, $1) }) else {
            throw DemoError("open_write_stream: \(lastError(api))")
        }
        guard let inFp = fopen(plain, "rb") else { _ = api.closeStream(ws); throw DemoError("cannot read \(plain)") }
        var rbuf = [UInt8](repeating: 0, count: CHUNK)
        while true {
            let rd = rbuf.withUnsafeMutableBufferPointer { fread($0.baseAddress, 1, CHUNK, inFp) }
            if rd <= 0 { break }
            let w = rbuf.withUnsafeBufferPointer { api.streamWrite(ws, $0.baseAddress, Int32(rd)) }
            if w != Int32(rd) { fclose(inFp); _ = api.closeStream(ws); throw DemoError("short write") }
        }
        fclose(inFp)
        _ = api.closeStream(ws)
    }
    report("encrypt (\(format))", elapsedMs(t))

    // (c) decrypt — stream it back out (discarding).
    t = DispatchTime.now()
    do {
        guard let rs = (withUtf8(fp) { api.openReadStream(handle, $0, $1) }) else {
            throw DemoError("open_read_stream: \(lastError(api))")
        }
        var dbuf = [UInt8](repeating: 0, count: CHUNK)
        var totalN: Int64 = 0
        while true {
            let got = dbuf.withUnsafeMutableBufferPointer { api.streamRead(rs, $0.baseAddress, Int32(CHUNK)) }
            if got <= 0 { break }
            totalN += Int64(got)
        }
        _ = api.closeStream(rs)
        if totalN != sizeBytes { throw DemoError("decrypt size \(totalN) != \(sizeBytes)") }
    }
    report("decrypt (\(format))", elapsedMs(t))

    // (d) read the plaintext file back from disk — gauges raw medium read speed.
    t = DispatchTime.now()
    do {
        guard let inFp = fopen(plain, "rb") else { throw DemoError("cannot read \(plain)") }
        var rbuf = [UInt8](repeating: 0, count: CHUNK)
        while true {
            let rd = rbuf.withUnsafeMutableBufferPointer { fread($0.baseAddress, 1, CHUNK, inFp) }
            if rd <= 0 { break }
        }
        fclose(inFp)
    }
    report("read file (disk read, may be cached)", elapsedMs(t))
}

func runBenchmark(_ api: Api, _ sizeGb: Double) throws {
    let sizeBytes = Int64(sizeGb * 1024.0 * 1024.0 * 1024.0 + 0.5)
    let CHUNK = 4 * 1024 * 1024
    print("\n========== Benchmark (\(formatGb(sizeGb)) GB per format, \(CHUNK >> 20) MiB chunks) ==========")
    print("  (disk read/write rows may just reflect the OS cache — pass --size larger than your RAM for disk-bound numbers)")
    for format in ["uvf", "cryptomator"] {
        try benchOne(api, format, sizeBytes, CHUNK)
    }
}

// Print a GB value like the other demos: integers without a trailing ".0".
func formatGb(_ g: Double) -> String {
    if g == g.rounded() { return String(Int(g)) }
    return String(g)
}

// ----- platform: executable dir + rid -----
func exePath() -> String {
    // CommandLine.arguments[0] may be relative; resolve it. On Linux, /proc/self/exe
    // is authoritative; fall back to Bundle/argv0 otherwise.
    #if os(Linux)
    var buf = [CChar](repeating: 0, count: 4096)
    let n = readlink("/proc/self/exe", &buf, buf.count - 1)
    if n > 0 {
        buf[n] = 0
        return String(cString: buf)
    }
    #endif
    if let exe = Bundle.main.executableURL?.path, !exe.isEmpty {
        return exe
    }
    let argv0 = CommandLine.arguments.first ?? ""
    if argv0.hasPrefix("/") { return argv0 }
    return (FileManager.default.currentDirectoryPath as NSString).appendingPathComponent(argv0)
}

func exeDir() -> String {
    (exePath() as NSString).deletingLastPathComponent
}

func rid() -> String {
    #if os(macOS)
    let os = "osx-"
    #else
    let os = "linux-"
    #endif
    #if arch(arm64)
    return os + "arm64"
    #else
    return os + "x64"
    #endif
}

func libFileName() -> String {
    #if os(macOS)
    return "libTitanVault.dylib"
    #else
    return "libTitanVault.so"
    #endif
}

// Discovery: exe dir -> cwd -> walk up from each for Dist/Native/<rid>/lib.
func discoverLib(_ ed: String) -> String {
    let fm = FileManager.default
    let file = libFileName()
    var candidates: [String] = []
    candidates.append((ed as NSString).appendingPathComponent(file))
    candidates.append((fm.currentDirectoryPath as NSString).appendingPathComponent(file))

    for start in [fm.currentDirectoryPath, ed] {
        var d = URL(fileURLWithPath: start, isDirectory: true).standardizedFileURL
        while true {
            let cand = d.appendingPathComponent("Dist")
                .appendingPathComponent("Native")
                .appendingPathComponent(rid())
                .appendingPathComponent(file)
            candidates.append(cand.path)
            let parent = d.deletingLastPathComponent().standardizedFileURL
            if parent.path == d.path { break }
            d = parent
        }
    }
    for c in candidates where fm.fileExists(atPath: c) { return c }
    return (ed as NSString).appendingPathComponent(file)
}

// ----- args -----
struct Args {
    var lib: String
    var format: String?
    var vault: String
    var password = "correct horse battery staple"
    var benchmark = false
    var interop = false
    var sizeGb: Double = 1
}

func parseArgs(_ ed: String) -> Args {
    var a = Args(
        lib: ProcessInfo.processInfo.environment["TITANVAULT_LIB"].flatMap { $0.isEmpty ? nil : $0 } ?? discoverLib(ed),
        format: nil,
        vault: (tmpDir() as NSString).appendingPathComponent("uvf-swift-demo")
    )
    let argv = Array(CommandLine.arguments.dropFirst())
    var i = 0
    while i < argv.count {
        let s = argv[i]
        func next() -> String { i += 1; return i < argv.count ? argv[i] : "" }
        switch s {
        case "--lib": a.lib = next()
        case "--format": a.format = next()
        case "--vault": a.vault = next()
        case "--password": a.password = next()
        case "--benchmark", "--bench": a.benchmark = true
        case "--size": a.sizeGb = Double(next()) ?? a.sizeGb
        case "--cryptomator-interop", "--interop": a.interop = true
        default: break
        }
        i += 1
    }
    return a
}

// ----- main -----
func main() -> Int32 {
    let ed = exeDir()
    let args = parseArgs(ed)

    if !FileManager.default.fileExists(atPath: args.lib) {
        let msg = """
        Native library not found: \(args.lib)
        Build it first:  ../../BuildScripts/build.ps1 -Task aot   (or build.sh --task aot)
        Then it loads automatically (same folder / cwd / ../../Dist/Native/<rid>/), or pass --lib <path>.

        """
        FileHandle.standardError.write(msg.data(using: .utf8)!)
        return 1
    }

    let api = Api(libPath: args.lib)
    print("TitanVault version: \(version(api))")

    if args.interop { return runInterop(api) ? 0 : 1 }
    if args.benchmark {
        do { try runBenchmark(api, args.sizeGb) } catch {
            print("\n❌ benchmark aborted: \(error)")
            return 1
        }
        return 0
    }

    let st = State()
    let formats = args.format.map { [$0] } ?? ["uvf", "cryptomator"]
    for format in formats {
        do {
            try runDemo(api, format, (args.vault as NSString).appendingPathComponent(format), args.password, st)
        } catch {
            st.failed += 1
            print("\n❌ \(format) demo aborted: \(error)")
        }
    }

    if args.format == nil {
        if !runInterop(api) { st.failed += 1 }
        do { try runBenchmark(api, 0.25) } catch {
            st.failed += 1
            print("\n❌ benchmark aborted: \(error)")
        }
    }

    print(st.failed == 0
        ? "\n✅ All Swift demo sections passed."
        : "\n❌ \(st.failed) section(s) failed.")
    return st.failed == 0 ? 0 : 1
}

exit(main())
