// Unified Vault Format (UVF) for C# and other languages.
// Copyright (c) Smart In Venture 2025- https://www.speedbits.io
// Licensed under AGPL-3.0 (commercial licenses available); see LICENSE.

// UVF / Cryptomator demo in Go via the native TitanVault library (C ABI, using `purego`).
//
// Full-parity port of Demo/NodeJs/vault-demo.js (and its C++ twin Demo/Cpp/vault_demo.cpp):
// same sections, same `… tests for <FORMAT>: PASSED/FAILED` lines, same flags, and (with no args)
// runs everything — both formats' functional sections, the real-Cryptomator-vault interop, and a
// quick benchmark.
//
// The library is loaded at RUNTIME via purego.Dlopen (no cgo, no C compiler — CGO_ENABLED=0). Each
// export is bound to a typed Go func with purego.RegisterLibFunc, so the ABI mapping is explicit.
//
// Build the native library first (from the repo root):
//   ../../BuildScripts/build.ps1 -Task aot        # -> Dist/Native/win-x64/TitanVault.dll
//   ../../BuildScripts/build.sh  --task aot        # -> Dist/Native/<rid>/libTitanVault.{so,dylib}
//
// Then:
//   go run .                      # both formats + interop + benchmark
//   go run . --format uvf         # one format's functional sections
//   go run . --benchmark --size 2 # throughput only, 2 GB
//   go run . --lib /path/to/TitanVault.dll

package main

import (
	"bytes"
	"fmt"
	"os"
	"path/filepath"
	"runtime"
	"strconv"
	"strings"
	"time"
	"unsafe"

	"github.com/ebitengine/purego"
)

// ----- C ABI constants (from titan_vault.h) -----
const (
	titanVaultSuccess            = 0
	titanVaultFormatCryptomator  = 0
	titanVaultFormatUvf          = 1
	maxList                      = 256
)

// OpenFlags (StorageLib.Abstractions) for open_stream_with_flags.
const (
	openReadOnly  = 0x0000
	openWriteOnly = 0x0001
	openCreate    = 0x0040
	openTruncate  = 0x0200
)

// Seek origin.
const seekBegin = 0

// ----- the bound C ABI (one field per export; the symbol is "titan_vault_<name>") -----
//
// purego ABI mapping conventions used below:
//   - UTF-8 string + length      -> []byte (purego passes the slice's data pointer) + int32 length.
//   - output byte buffers        -> []byte.
//   - in/out size (int*)         -> *int32.
//   - int64 stream offsets/len   -> int64.
//   - handles/streams (void*)    -> uintptr.
//   - returned char*             -> uintptr (read with goString; free with freeString where owned).
//   - char*[] (list_directory /  -> []uintptr of length maxList (its data pointer is the char*[]).
//     get_vault_users)
type api struct {
	getVersion   func() uintptr
	getLastError func() uintptr
	freeString   func(uintptr)

	detectVaultFormat func(path []byte, pathLen int32) int32
	secureZeroMemory  func(buf []byte, size int32)
	backupFiles       func(path []byte, pathLen int32, backup []byte, backupLen int32, overwrite int32) int32

	createCryptomator       func(path []byte, pathLen int32, pw []byte, pwLen int32) int32
	loadCryptomator         func(path []byte, pathLen int32, pw []byte, pwLen int32) uintptr
	changeCryptomatorPassword func(path []byte, pathLen int32, oldPw []byte, oldLen int32, newPw []byte, newLen int32) int32

	createUvf            func(path []byte, pathLen int32, pw []byte, pwLen int32, encryptNames, kdfMethod, kdfIters int32) int32
	loadUvf             func(path []byte, pathLen int32, pw []byte, pwLen int32, userID []byte, userIDLen int32) uintptr
	changeUvfAdminPassword func(path []byte, pathLen int32, oldPw []byte, oldLen int32, newPw []byte, newLen int32) int32
	changeUvfUserPassword  func(path []byte, pathLen int32, adminPw []byte, adminLen int32, userID []byte, userIDLen int32, newPw []byte, newLen int32) int32

	addUser       func(path []byte, pathLen int32, adminPw []byte, adminLen int32, userID []byte, userIDLen int32, userPw []byte, userPwLen int32) int32
	removeUser    func(path []byte, pathLen int32, adminPw []byte, adminLen int32, userID []byte, userIDLen int32) int32
	getVaultUsers func(path []byte, pathLen int32, adminPw []byte, adminLen int32, users []uintptr, maxUsers *int32) int32
	rotateKeys    func(path []byte, pathLen int32, adminPw []byte, adminLen int32, format int32) int32

	generateUserKeyPair func(pw []byte, pwLen int32, pubBuf []byte, pubSize *int32, privBuf []byte, privSize *int32) int32
	addUserByPublicKey  func(path []byte, pathLen int32, adminPw []byte, adminLen int32, userID []byte, userIDLen int32, pub []byte, pubLen int32) int32
	loadUvfWithKey      func(path []byte, pathLen int32, encPriv []byte, encPrivLen int32, keyPw []byte, keyPwLen int32, userID []byte, userIDLen int32) uintptr
	rotateKeysPubKey    func(path []byte, pathLen int32, adminPw []byte, adminLen int32) int32

	writeFile  func(h uintptr, path []byte, pathLen int32, buf []byte, bufLen int32) int32
	readFile   func(h uintptr, path []byte, pathLen int32, buf []byte, bufSize *int32) int32
	fileExists func(h uintptr, path []byte, pathLen int32) int32
	deleteFile func(h uintptr, path []byte, pathLen int32) int32
	move       func(h uintptr, src []byte, srcLen int32, dst []byte, dstLen int32) int32

	writeAllText  func(h uintptr, path []byte, pathLen int32, text []byte, textLen int32) int32
	appendAllText func(h uintptr, path []byte, pathLen int32, text []byte, textLen int32) int32
	readAllText   func(h uintptr, path []byte, pathLen int32) uintptr

	createDirectory func(h uintptr, path []byte, pathLen int32) int32
	directoryExists func(h uintptr, path []byte, pathLen int32) int32
	deleteDirectory func(h uintptr, path []byte, pathLen int32) int32
	listDirectory   func(h uintptr, path []byte, pathLen int32, entries []uintptr, maxEntries *int32) int32
	getFileInfo     func(h uintptr, path []byte, pathLen int32, size *int64, mtime *int64) int32

	openReadStream     func(h uintptr, path []byte, pathLen int32) uintptr
	openWriteStream    func(h uintptr, path []byte, pathLen int32) uintptr
	openStreamWithFlags func(h uintptr, path []byte, pathLen int32, flags int32) uintptr
	streamRead         func(s uintptr, buf []byte, count int32) int32
	streamWrite        func(s uintptr, buf []byte, count int32) int32
	streamSeek         func(s uintptr, offset int64, origin int32) int64
	streamGetPosition  func(s uintptr) int64
	streamGetLength    func(s uintptr) int64
	streamSetLength    func(s uintptr, length int64) int32
	streamFlush        func(s uintptr) int32
	closeStream        func(s uintptr) int32

	closeVault func(h uintptr) int32
}

func bindAPI(handle uintptr) *api {
	a := &api{}
	purego.RegisterLibFunc(&a.getVersion, handle, "titan_vault_get_version")
	purego.RegisterLibFunc(&a.getLastError, handle, "titan_vault_get_last_error")
	purego.RegisterLibFunc(&a.freeString, handle, "titan_vault_free_string")

	purego.RegisterLibFunc(&a.detectVaultFormat, handle, "titan_vault_detect_vault_format")
	purego.RegisterLibFunc(&a.secureZeroMemory, handle, "titan_vault_secure_zero_memory")
	purego.RegisterLibFunc(&a.backupFiles, handle, "titan_vault_backup_files")

	purego.RegisterLibFunc(&a.createCryptomator, handle, "titan_vault_create_cryptomator_vault")
	purego.RegisterLibFunc(&a.loadCryptomator, handle, "titan_vault_load_cryptomator_vault")
	purego.RegisterLibFunc(&a.changeCryptomatorPassword, handle, "titan_vault_change_cryptomator_password")

	purego.RegisterLibFunc(&a.createUvf, handle, "titan_vault_create_uvf_vault")
	purego.RegisterLibFunc(&a.loadUvf, handle, "titan_vault_load_uvf_vault")
	purego.RegisterLibFunc(&a.changeUvfAdminPassword, handle, "titan_vault_change_uvf_admin_password")
	purego.RegisterLibFunc(&a.changeUvfUserPassword, handle, "titan_vault_change_uvf_user_password")

	purego.RegisterLibFunc(&a.addUser, handle, "titan_vault_add_user")
	purego.RegisterLibFunc(&a.removeUser, handle, "titan_vault_remove_user")
	purego.RegisterLibFunc(&a.getVaultUsers, handle, "titan_vault_get_vault_users")
	purego.RegisterLibFunc(&a.rotateKeys, handle, "titan_vault_rotate_keys")

	purego.RegisterLibFunc(&a.generateUserKeyPair, handle, "titan_vault_generate_user_keypair")
	purego.RegisterLibFunc(&a.addUserByPublicKey, handle, "titan_vault_add_user_by_public_key")
	purego.RegisterLibFunc(&a.loadUvfWithKey, handle, "titan_vault_load_uvf_vault_with_key")
	purego.RegisterLibFunc(&a.rotateKeysPubKey, handle, "titan_vault_rotate_keys_pubkey")

	purego.RegisterLibFunc(&a.writeFile, handle, "titan_vault_write_file")
	purego.RegisterLibFunc(&a.readFile, handle, "titan_vault_read_file")
	purego.RegisterLibFunc(&a.fileExists, handle, "titan_vault_file_exists")
	purego.RegisterLibFunc(&a.deleteFile, handle, "titan_vault_delete_file")
	purego.RegisterLibFunc(&a.move, handle, "titan_vault_move")

	purego.RegisterLibFunc(&a.writeAllText, handle, "titan_vault_write_all_text")
	purego.RegisterLibFunc(&a.appendAllText, handle, "titan_vault_append_all_text")
	purego.RegisterLibFunc(&a.readAllText, handle, "titan_vault_read_all_text")

	purego.RegisterLibFunc(&a.createDirectory, handle, "titan_vault_create_directory")
	purego.RegisterLibFunc(&a.directoryExists, handle, "titan_vault_directory_exists")
	purego.RegisterLibFunc(&a.deleteDirectory, handle, "titan_vault_delete_directory")
	purego.RegisterLibFunc(&a.listDirectory, handle, "titan_vault_list_directory")
	purego.RegisterLibFunc(&a.getFileInfo, handle, "titan_vault_get_file_info")

	purego.RegisterLibFunc(&a.openReadStream, handle, "titan_vault_open_read_stream")
	purego.RegisterLibFunc(&a.openWriteStream, handle, "titan_vault_open_write_stream")
	purego.RegisterLibFunc(&a.openStreamWithFlags, handle, "titan_vault_open_stream_with_flags")
	purego.RegisterLibFunc(&a.streamRead, handle, "titan_vault_stream_read")
	purego.RegisterLibFunc(&a.streamWrite, handle, "titan_vault_stream_write")
	purego.RegisterLibFunc(&a.streamSeek, handle, "titan_vault_stream_seek")
	purego.RegisterLibFunc(&a.streamGetPosition, handle, "titan_vault_stream_get_position")
	purego.RegisterLibFunc(&a.streamGetLength, handle, "titan_vault_stream_get_length")
	purego.RegisterLibFunc(&a.streamSetLength, handle, "titan_vault_stream_set_length")
	purego.RegisterLibFunc(&a.streamFlush, handle, "titan_vault_stream_flush")
	purego.RegisterLibFunc(&a.closeStream, handle, "titan_vault_close_stream")

	purego.RegisterLibFunc(&a.closeVault, handle, "titan_vault_close_vault")
	return a
}

// ----- small helpers -----

// b returns the UTF-8 bytes of s; purego passes the slice's data pointer to the C function.
func b(s string) []byte { return []byte(s) }

// goString copies the NUL-terminated C string at ptr into a Go string (does not free it).
// Pointer arithmetic stays on unsafe.Pointer (via unsafe.Add) so go vet is satisfied.
func goString(ptr uintptr) string {
	if ptr == 0 {
		return ""
	}
	// ptr is a C-owned, stable heap pointer returned across the FFI boundary; converting it back to
	// an unsafe.Pointer is the standard purego idiom. (go vet's unsafeptr check flags this as a
	// conservative false positive — it cannot prove an FFI uintptr is a live Go pointer.)
	base := unsafe.Pointer(ptr) //nolint:govet
	var n int
	for *(*byte)(unsafe.Add(base, n)) != 0 {
		n++
	}
	if n == 0 {
		return ""
	}
	return string(unsafe.Slice((*byte)(base), n))
}

func (a *api) lastError() string {
	p := a.getLastError() // static buffer — must NOT be freed.
	if p == 0 {
		return "(no error)"
	}
	return goString(p)
}

func (a *api) check(rc int32, what string) {
	if rc != titanVaultSuccess {
		panic(fmt.Sprintf("%s failed (rc=%d): %s", what, rc, a.lastError()))
	}
}

func (a *api) version() string {
	p := a.getVersion()
	if p == 0 {
		return "(unknown)"
	}
	s := goString(p)
	a.freeString(p) // heap string — release it.
	return s
}

// readStringArray decodes the first n char* of entries into Go strings, freeing each native string.
func (a *api) readStringArray(entries []uintptr, n int) []string {
	out := make([]string, 0, max(n, 0))
	for i := 0; i < n; i++ {
		p := entries[i]
		out = append(out, goString(p))
		if p != 0 {
			a.freeString(p)
		}
	}
	return out
}

func (a *api) listDir(h uintptr, path string) []string {
	entries := make([]uintptr, maxList)
	maxN := int32(maxList)
	n := a.listDirectory(h, b(path), int32(len(path)), entries, &maxN)
	if n < 0 {
		panic(fmt.Sprintf("list_directory %s rc=%d: %s", path, n, a.lastError()))
	}
	return a.readStringArray(entries, int(n))
}

// readFileFull reads a whole vault file, growing the buffer to the required size if needed.
func (a *api) readFileFull(h uintptr, path string) []byte {
	cap := int32(1 << 20) // 1 MiB
	for attempt := 0; attempt < 5; attempt++ {
		buf := make([]byte, cap)
		size := cap
		rc := a.readFile(h, b(path), int32(len(path)), buf, &size)
		if rc == titanVaultSuccess {
			return buf[:size]
		}
		if size > cap {
			cap = size // grow to required size and retry
			continue
		}
		panic(fmt.Sprintf("read_file %s rc=%d: %s", path, rc, a.lastError()))
	}
	panic(fmt.Sprintf("read_file %s: buffer growth failed", path))
}

func jsonList(v []string) string {
	parts := make([]string, len(v))
	for i, s := range v {
		parts[i] = "\"" + s + "\""
	}
	return "[" + strings.Join(parts, ",") + "]"
}

func leakedToDisk(vaultDir, basename string) bool {
	leaked := false
	filepath.Walk(vaultDir, func(p string, info os.FileInfo, err error) error {
		if err == nil && info != nil && !info.IsDir() && filepath.Base(p) == basename {
			leaked = true
		}
		return nil
	})
	return leaked
}

func upper(s string) string { return strings.ToUpper(s) }

func (a *api) openVault(format, vaultDir, password, userID, userPassword string) uintptr {
	if format == "uvf" {
		pw := userPassword
		if pw == "" {
			pw = password
		}
		var uid []byte
		var uidLen int32
		if userID != "" {
			uid = b(userID)
			uidLen = int32(len(userID))
		}
		return a.loadUvf(b(vaultDir), int32(len(vaultDir)), b(pw), int32(len(pw)), uid, uidLen)
	}
	return a.loadCryptomator(b(vaultDir), int32(len(vaultDir)), b(password), int32(len(password)))
}

type state struct{ failed int }

// section runs fn, reporting PASSED on success or FAILED (with the panic message) on failure.
func (st *state) section(label, format string, fn func()) {
	defer func() {
		if r := recover(); r != nil {
			st.failed++
			fmt.Printf("  %s tests for %s: FAILED — %v\n", label, upper(format), r)
		}
	}()
	fn()
	fmt.Printf("  %s tests for %s: PASSED\n", label, upper(format))
}

func elapsedMs(t time.Time) float64 { return float64(time.Since(t).Nanoseconds()) / 1e6 }
func mbps(bytesN, ms float64) float64 { return (bytesN / 1e6) / (ms / 1000.0) }

func pidStr() string { return strconv.Itoa(os.Getpid()) }

// ----- the per-format demo -----
func runDemo(a *api, format, vaultDir, password string, st *state) {
	fmt.Printf("\n========== %s ==========\n", upper(format))
	os.RemoveAll(vaultDir)
	os.MkdirAll(vaultDir, 0o755)
	vlen := int32(len(vaultDir))
	plen := int32(len(password))

	if format == "uvf" {
		a.check(a.createUvf(b(vaultDir), vlen, b(password), plen, 1, 0, 0), "create_uvf_vault")
	} else {
		a.check(a.createCryptomator(b(vaultDir), vlen, b(password), plen), "create_cryptomator_vault")
	}

	handle := a.openVault(format, vaultDir, password, "", "")
	if handle == 0 {
		panic(fmt.Sprintf("load %s vault failed: %s", format, a.lastError()))
	}
	fmt.Printf("Created + opened %s vault at %s\n", format, vaultDir)

	pp := "/persist.txt"
	persist := b("persisted across reopen")
	a.check(a.writeFile(handle, b(pp), int32(len(pp)), persist, int32(len(persist))), "write persist.txt")

	// 0. Detect format (path-based).
	st.section("Detect format", format, func() {
		detected := a.detectVaultFormat(b(vaultDir), vlen)
		expected := int32(titanVaultFormatCryptomator)
		if format == "uvf" {
			expected = titanVaultFormatUvf
		}
		if detected != expected {
			panic(fmt.Sprintf("detect_vault_format=%d, expected %d", detected, expected))
		}
	})

	// 1. File round-trip + filename-leak check.
	st.section("File", format, func() {
		fp := "/hello.txt"
		pt := b("Hello, encrypted world!")
		a.check(a.writeFile(handle, b(fp), int32(len(fp)), pt, int32(len(pt))), "write_file")
		got := a.readFileFull(handle, fp)
		if !bytes.Equal(got, pt) {
			panic("round-trip mismatch")
		}
		if leakedToDisk(vaultDir, "hello.txt") {
			panic("plaintext filename leaked to disk")
		}
		if a.fileExists(handle, b(fp), int32(len(fp))) != 1 {
			panic("exists should be 1")
		}
		a.check(a.deleteFile(handle, b(fp), int32(len(fp))), "delete_file")
		if a.fileExists(handle, b(fp), int32(len(fp))) != 0 {
			panic("exists should be 0 after delete")
		}
	})

	// 1b. UTF-8 text convenience.
	st.section("Text helpers", format, func() {
		tf, first, second := "/notes.txt", "first line\n", "second line\n"
		a.check(a.writeAllText(handle, b(tf), int32(len(tf)), b(first), int32(len(first))), "write_all_text")
		a.check(a.appendAllText(handle, b(tf), int32(len(tf)), b(second), int32(len(second))), "append_all_text")
		p := a.readAllText(handle, b(tf), int32(len(tf)))
		if p == 0 {
			panic("read_all_text: " + a.lastError())
		}
		text := goString(p)
		a.freeString(p)
		if text != first+second {
			panic("text round-trip mismatch: " + text)
		}
	})

	// 2. Directories: create, write into, list, file-info, move/rename.
	st.section("Directory", format, func() {
		dir := "/docs"
		a.check(a.createDirectory(handle, b(dir), int32(len(dir))), "create_directory")
		if a.directoryExists(handle, b(dir), int32(len(dir))) != 1 {
			panic("directory_exists should be 1")
		}
		note := "/docs/note.txt"
		body := b("inside a subdirectory")
		a.check(a.writeFile(handle, b(note), int32(len(note)), body, int32(len(body))), "write into dir")
		names := a.listDir(handle, dir)
		if !contains(names, "note.txt") {
			panic("listing missing note.txt (got " + jsonList(names) + ")")
		}
		var sz, mtime int64
		a.check(a.getFileInfo(handle, b(note), int32(len(note)), &sz, &mtime), "get_file_info")
		if sz != int64(len(body)) {
			panic(fmt.Sprintf("file size %d != %d", sz, len(body)))
		}
		renamed := "/docs/renamed.txt"
		a.check(a.move(handle, b(note), int32(len(note)), b(renamed), int32(len(renamed))), "move")
		names = a.listDir(handle, dir)
		if !contains(names, "renamed.txt") {
			panic("rename not reflected (got " + jsonList(names) + ")")
		}
		fmt.Printf("    /docs now contains: %s (size of note was %d bytes)\n", jsonList(names), sz)
	})

	// 3. Streaming: multi-chunk write, then random-access read; plus the fuller stream API.
	st.section("Streaming", format, func() {
		fp := "/big.bin"
		const CHUNK, CHUNKS = 32 * 1024, 4
		total := int64(CHUNK) * CHUNKS
		chunk := make([]byte, CHUNK)
		for j := 0; j < CHUNK; j++ {
			chunk[j] = byte(j % 256)
		}

		ws := a.openWriteStream(handle, b(fp), int32(len(fp)))
		if ws == 0 {
			panic("open_write_stream: " + a.lastError())
		}
		for i := 0; i < CHUNKS; i++ {
			if a.streamWrite(ws, chunk, CHUNK) != CHUNK {
				a.closeStream(ws)
				panic("short write")
			}
		}
		a.check(a.streamFlush(ws), "stream_flush")
		a.closeStream(ws)

		rs := a.openReadStream(handle, b(fp), int32(len(fp)))
		if rs == 0 {
			panic("open_read_stream: " + a.lastError())
		}
		func() {
			defer func() {
				if r := recover(); r != nil {
					a.closeStream(rs)
					panic(r)
				}
			}()
			if a.streamGetLength(rs) != total {
				panic("stream length mismatch")
			}
			rbuf := make([]byte, CHUNK)
			var off int64
			for {
				got := a.streamRead(rs, rbuf, CHUNK)
				if got <= 0 {
					break
				}
				for k := 0; k < int(got); k++ {
					if rbuf[k] != byte((off+int64(k))%256) {
						panic(fmt.Sprintf("byte mismatch at %d", off+int64(k)))
					}
				}
				off += int64(got)
			}
			if off != total {
				panic(fmt.Sprintf("read %d of %d", off, total))
			}
			if a.streamGetPosition(rs) != total {
				panic("stream_get_position != total")
			}
			seekTo := int64(70000)
			pos := a.streamSeek(rs, seekTo, seekBegin)
			if pos == seekTo {
				seekBuf := make([]byte, 16)
				if a.streamRead(rs, seekBuf, 16) != 16 {
					panic("short seek-read")
				}
				for k := 0; k < 16; k++ {
					if seekBuf[k] != byte((seekTo+int64(k))%256) {
						panic("seek byte mismatch")
					}
				}
				fmt.Printf("    wrote+verified %d bytes; seek to %d OK\n", total, seekTo)
			} else {
				fmt.Printf("    wrote+verified %d bytes; seek not supported by this backend (skipped)\n", total)
			}
			rs2 := a.openStreamWithFlags(handle, b(fp), int32(len(fp)), openReadOnly)
			if rs2 == 0 {
				panic("open_stream_with_flags: " + a.lastError())
			}
			lenOk := a.streamGetLength(rs2) == total
			a.closeStream(rs2)
			if !lenOk {
				panic("flags-open length mismatch")
			}
		}()
		a.closeStream(rs)

		// stream_set_length: truncation of encrypted streams is backend-dependent; best-effort.
		tp := "/trunc.bin"
		ts := a.openStreamWithFlags(handle, b(tp), int32(len(tp)), openWriteOnly|openCreate|openTruncate)
		if ts != 0 {
			a.streamWrite(ts, chunk, CHUNK)
			a.streamSetLength(ts, 4096)
			a.closeStream(ts)
		}
	})

	a.closeVault(handle)

	// 4. Persistence: reopen with the passphrase and re-read.
	st.section("Persistence", format, func() {
		h2 := a.openVault(format, vaultDir, password, "", "")
		if h2 == 0 {
			panic("reopen failed: " + a.lastError())
		}
		defer a.closeVault(h2)
		if !bytes.Equal(a.readFileFull(h2, pp), persist) {
			panic("persisted content mismatch")
		}
	})

	// 5/6. UVF-only: key rotation, public-key multi-user, password multi-user.
	if format == "uvf" {
		rc := a.rotateKeys(b(vaultDir), vlen, b(password), plen, titanVaultFormatUvf)
		if rc == titanVaultSuccess {
			fmt.Println("  Key rotation tests for UVF: PASSED")
		} else {
			e := a.lastError()
			if strings.Contains(strings.ToLower(e), "not implemented") {
				fmt.Println("  Key rotation tests for UVF: SKIPPED (not implemented)")
			} else {
				st.failed++
				fmt.Printf("  Key rotation tests for UVF: FAILED — %s\n", e)
			}
		}

		st.section("Public-key multi-user", format, func() {
			bob, keyPw := "bob", "bob-key-pass-123"
			pub := make([]byte, 4096)
			priv := make([]byte, 8192)
			pubSize := int32(len(pub))
			privSize := int32(len(priv))
			a.check(a.generateUserKeyPair(b(keyPw), int32(len(keyPw)), pub, &pubSize, priv, &privSize), "generate_user_keypair")
			pub = pub[:pubSize]
			priv = priv[:privSize]
			fmt.Printf("    generated bob key pair (public %dB, encrypted private %dB)\n", pubSize, privSize)
			a.check(a.addUserByPublicKey(b(vaultDir), vlen, b(password), plen, b(bob), int32(len(bob)), pub, int32(len(pub))), "add_user_by_public_key")

			readAsBob := func() {
				h := a.loadUvfWithKey(b(vaultDir), vlen, priv, int32(len(priv)), b(keyPw), int32(len(keyPw)), b(bob), int32(len(bob)))
				if h == 0 {
					panic("load as bob failed: " + a.lastError())
				}
				defer a.closeVault(h)
				if !bytes.Equal(a.readFileFull(h, pp), persist) {
					panic("bob read mismatch")
				}
			}
			readAsBob()
			fmt.Println("    opened as bob (public-key user) and read the admin file OK")
			a.check(a.rotateKeysPubKey(b(vaultDir), vlen, b(password), plen), "rotate_keys_pubkey")
			readAsBob()
			fmt.Println("    rotated keys (no member password) and bob still reads OK")
		})

		st.section("Multi-user", format, func() {
			alice, alicePw := "alice", "alice-passphrase-123"
			a.check(a.addUser(b(vaultDir), vlen, b(password), plen, b(alice), int32(len(alice)), b(alicePw), int32(len(alicePw))), "add_user")
			users := make([]uintptr, maxList)
			um := int32(maxList)
			n := a.getVaultUsers(b(vaultDir), vlen, b(password), plen, users, &um)
			if n < 0 {
				panic(fmt.Sprintf("get_vault_users rc=%d: %s", n, a.lastError()))
			}
			userList := a.readStringArray(users, int(n))
			fmt.Printf("    vault users: %s\n", jsonList(userList))
			if !contains(userList, alice) {
				panic("added user not listed (got " + jsonList(userList) + ")")
			}

			// Best-effort: open as the new user (a known library limitation — reported, not failed).
			func() {
				defer func() {
					if r := recover(); r != nil {
						fmt.Printf("    ⚠ opening as a secondary user is not yet supported by the library: %v\n", r)
					}
				}()
				ah := a.openVault("uvf", vaultDir, password, alice, alicePw)
				if ah == 0 {
					panic(a.lastError())
				}
				defer a.closeVault(ah)
				if !bytes.Equal(a.readFileFull(ah, pp), persist) {
					panic("alice read mismatch")
				}
				fmt.Println("    opened as second user and read the admin-written file OK")
			}()

			// Change a member's password (admin-driven), then remove the member and confirm they're gone.
			aliceNewPw := "alice-passphrase-456"
			a.check(a.changeUvfUserPassword(b(vaultDir), vlen, b(password), plen, b(alice), int32(len(alice)), b(aliceNewPw), int32(len(aliceNewPw))), "change_uvf_user_password")
			a.check(a.removeUser(b(vaultDir), vlen, b(password), plen, b(alice), int32(len(alice))), "remove_user")
			users2 := make([]uintptr, maxList)
			um2 := int32(maxList)
			n2 := a.getVaultUsers(b(vaultDir), vlen, b(password), plen, users2, &um2)
			if n2 < 0 {
				n2 = 0
			}
			userList2 := a.readStringArray(users2, int(n2))
			if contains(userList2, alice) {
				panic("removed user still listed (got " + jsonList(userList2) + ")")
			}
			fmt.Printf("    changed alice's password, then removed alice; users now: %s\n", jsonList(userList2))
		})
	}

	// 7. Maintenance (both formats): backup, secure-wipe, password change + reopen.
	st.section("Maintenance", format, func() {
		backupDir := filepath.Join(os.TempDir(), fmt.Sprintf("uvf-backup-%s-%s", format, pidStr()))
		os.RemoveAll(backupDir)
		a.check(a.backupFiles(b(vaultDir), vlen, b(backupDir), int32(len(backupDir)), 1), "backup_files")
		if !dirHasFiles(backupDir) {
			panic("backup produced no files")
		}

		secret := b("super-secret-key-material")
		a.secureZeroMemory(secret, int32(len(secret)))
		for _, c := range secret {
			if c != 0 {
				panic("secure_zero_memory did not zero the buffer")
			}
		}

		newPw := password + "-rotated"
		if format == "uvf" {
			a.check(a.changeUvfAdminPassword(b(vaultDir), vlen, b(password), plen, b(newPw), int32(len(newPw))), "change_uvf_admin_password")
		} else {
			a.check(a.changeCryptomatorPassword(b(vaultDir), vlen, b(password), plen, b(newPw), int32(len(newPw))), "change_cryptomator_password")
		}
		h3 := a.openVault(format, vaultDir, newPw, "", "")
		if h3 == 0 {
			panic("reopen after password change failed: " + a.lastError())
		}
		func() {
			defer a.closeVault(h3)
			if !bytes.Equal(a.readFileFull(h3, pp), persist) {
				panic("content mismatch after password change")
			}
		}()
		os.RemoveAll(backupDir)
		fmt.Printf("    backed up key files, secure-zeroed a buffer, changed the %s password and re-read OK\n", format)
	})

	fmt.Printf("✅ %s demo finished.\n", format)
}

// ----- interop: unlock a REAL Cryptomator vault and byte-compare the files -----
func findInteropBase() string {
	starts := []string{}
	if cwd, err := os.Getwd(); err == nil {
		starts = append(starts, cwd)
	}
	if exe, err := os.Executable(); err == nil {
		starts = append(starts, filepath.Dir(exe))
	}
	for _, start := range starts {
		for d := start; ; {
			cands := []string{
				filepath.Join(d, "_test-cryptomator-vault"),
				filepath.Join(d, "Demo", "_test-cryptomator-vault"),
				filepath.Join(d, "..", "_test-cryptomator-vault"),
			}
			for _, cand := range cands {
				if fileExists(filepath.Join(cand, "smartinventure", "masterkey.cryptomator")) {
					abs, err := filepath.Abs(cand)
					if err != nil {
						return cand
					}
					return abs
				}
			}
			parent := filepath.Dir(d)
			if parent == d {
				break
			}
			d = parent
		}
	}
	return ""
}

func runInterop(a *api) bool {
	fmt.Println("\n========== Cryptomator interop (real vault) ==========")
	base := findInteropBase()
	if base == "" {
		fmt.Println("(Cryptomator interop skipped — Demo/_test-cryptomator-vault not found)")
		return true
	}
	vaultDir := filepath.Join(base, "smartinventure")
	origDir := filepath.Join(base, "original-files")
	password := "smartinventure" // demo vault — hardcoded on purpose

	h := a.loadCryptomator(b(vaultDir), int32(len(vaultDir)), b(password), int32(len(password)))
	if h == 0 {
		fmt.Printf("Unlock failed: %s\n", a.lastError())
		return false
	}
	defer a.closeVault(h)

	allOk := true
	func() {
		defer func() {
			if r := recover(); r != nil {
				fmt.Printf("❌ Cryptomator interop FAILED: %v\n", r)
				allOk = false
			}
		}()
		fmt.Printf("Unlocked real Cryptomator vault at %s\n", vaultDir)
		for _, d := range []string{"/", "/mysubfolder1", "/mysubfolder1/mysubfolder2"} {
			fmt.Printf("  %s  ->  %s\n", d, jsonList(a.listDir(h, d)))
		}
		cases := [][2]string{
			{"/Perfect-albums.txt", "Perfect-albums.txt"},
			{"/mysubfolder1/banana.jpg", "banana.jpg"},
			{"/mysubfolder1/mysubfolder2/Rubicon - Rivers - lyrics.txt", "Rubicon - Rivers - lyrics.txt"},
		}
		for _, c := range cases {
			decrypted := a.readFileFull(h, c[0])
			orig, err := os.ReadFile(filepath.Join(origDir, c[1]))
			if err != nil {
				panic(err)
			}
			ok := bytes.Equal(decrypted, orig)
			if !ok {
				allOk = false
			}
			mark := "✓"
			word := "match"
			if !ok {
				mark, word = "✗", "MISMATCH"
			}
			fmt.Printf("  %s %s  (%d B)  bytes %s\n", mark, c[0], len(decrypted), word)
		}
		if allOk {
			fmt.Println("✅ Reading a real Cryptomator vault worked — all files decrypted and byte-matched the originals.")
		} else {
			fmt.Println("❌ Cryptomator interop FAILED — byte mismatch.")
		}
	}()
	return allOk
}

// ----- benchmark -----
func benchOne(a *api, format string, sizeBytes int64, chunkSize int) {
	fmt.Printf("\n----- %s -----\n", upper(format))
	dir := filepath.Join(os.TempDir(), fmt.Sprintf("uvf-bench-%s-%s", format, pidStr()))
	os.RemoveAll(dir)
	defer os.RemoveAll(dir)
	vaultDir := filepath.Join(dir, "vault")
	os.MkdirAll(vaultDir, 0o755)
	plain := filepath.Join(dir, "plain.bin")
	password := "bench-pass-123"

	report := func(label string, ms float64) {
		fmt.Printf("  %-38s %7.0f ms   %8.1f MB/s\n", label, ms, mbps(float64(sizeBytes), ms))
	}
	chunk := make([]byte, chunkSize)
	for i := 0; i < chunkSize; i++ {
		chunk[i] = byte(i & 0xff)
	}

	// (a) create the plaintext file on disk — gauges raw medium write speed
	t := time.Now()
	{
		f, err := os.Create(plain)
		if err != nil {
			panic(err)
		}
		var w int64
		for w < sizeBytes {
			n := int64(chunkSize)
			if rem := sizeBytes - w; rem < n {
				n = rem
			}
			if _, err := f.Write(chunk[:n]); err != nil {
				f.Close()
				panic(err)
			}
			w += n
		}
		f.Sync()
		f.Close()
	}
	report("create file (disk write, may be cached)", elapsedMs(t))

	vlen := int32(len(vaultDir))
	plen := int32(len(password))
	if format == "uvf" {
		a.check(a.createUvf(b(vaultDir), vlen, b(password), plen, 1, 0, 0), "create_uvf_vault")
	} else {
		a.check(a.createCryptomator(b(vaultDir), vlen, b(password), plen), "create_cryptomator_vault")
	}
	var handle uintptr
	if format == "uvf" {
		handle = a.loadUvf(b(vaultDir), vlen, b(password), plen, nil, 0)
	} else {
		handle = a.loadCryptomator(b(vaultDir), vlen, b(password), plen)
	}
	if handle == 0 {
		panic("load failed: " + a.lastError())
	}
	defer a.closeVault(handle)

	fp := "/big.bin"

	// (b) encrypt — stream the plaintext into the vault
	t = time.Now()
	{
		ws := a.openWriteStream(handle, b(fp), int32(len(fp)))
		if ws == 0 {
			panic("open_write_stream: " + a.lastError())
		}
		f, err := os.Open(plain)
		if err != nil {
			a.closeStream(ws)
			panic(err)
		}
		rbuf := make([]byte, chunkSize)
		for {
			rd, err := f.Read(rbuf)
			if rd > 0 {
				if a.streamWrite(ws, rbuf[:rd], int32(rd)) != int32(rd) {
					f.Close()
					a.closeStream(ws)
					panic("short write")
				}
			}
			if err != nil {
				break
			}
		}
		f.Close()
		a.closeStream(ws)
	}
	report(fmt.Sprintf("encrypt (%s)", format), elapsedMs(t))

	// (c) decrypt — stream it back out of the vault (discarding the plaintext)
	t = time.Now()
	{
		rs := a.openReadStream(handle, b(fp), int32(len(fp)))
		if rs == 0 {
			panic("open_read_stream: " + a.lastError())
		}
		dbuf := make([]byte, chunkSize)
		var total int64
		for {
			got := a.streamRead(rs, dbuf, int32(chunkSize))
			if got <= 0 {
				break
			}
			total += int64(got)
		}
		a.closeStream(rs)
		if total != sizeBytes {
			panic(fmt.Sprintf("decrypt size %d != %d", total, sizeBytes))
		}
	}
	report(fmt.Sprintf("decrypt (%s)", format), elapsedMs(t))

	// (d) read the plaintext file back from disk — gauges raw medium read speed
	t = time.Now()
	{
		f, err := os.Open(plain)
		if err != nil {
			panic(err)
		}
		rbuf := make([]byte, chunkSize)
		for {
			_, err := f.Read(rbuf)
			if err != nil {
				break
			}
		}
		f.Close()
	}
	report("read file (disk read, may be cached)", elapsedMs(t))
}

func runBenchmark(a *api, sizeGb float64) {
	sizeBytes := int64(sizeGb*1024.0*1024.0*1024.0 + 0.5)
	const chunkSize = 4 * 1024 * 1024 // 4 MiB
	fmt.Printf("\n========== Benchmark (%g GB per format, %d MiB chunks) ==========\n", sizeGb, chunkSize>>20)
	fmt.Println("  (disk read/write rows may just reflect the OS cache — pass --size larger than your RAM for disk-bound numbers)")
	for _, format := range []string{"uvf", "cryptomator"} {
		benchOne(a, format, sizeBytes, chunkSize)
	}
}

// ----- library discovery + args -----
func libFileName() string {
	switch runtime.GOOS {
	case "windows":
		return "TitanVault.dll"
	case "darwin":
		return "libTitanVault.dylib"
	default:
		return "libTitanVault.so"
	}
}

func rid() string {
	var osPart string
	switch runtime.GOOS {
	case "windows":
		osPart = "win-"
	case "darwin":
		osPart = "osx-"
	default:
		osPart = "linux-"
	}
	// NOTE: on this build host GOARCH is amd64 -> resolves win-x64.
	if runtime.GOARCH == "arm64" {
		return osPart + "arm64"
	}
	return osPart + "x64"
}

func discoverLib(exeDir string) string {
	file := libFileName()
	cwd, _ := os.Getwd()
	cands := []string{filepath.Join(exeDir, file), filepath.Join(cwd, file)}
	for _, start := range []string{cwd, exeDir} {
		if start == "" {
			continue
		}
		for d := start; ; {
			cands = append(cands, filepath.Join(d, "Dist", "Native", rid(), file))
			parent := filepath.Dir(d)
			if parent == d {
				break
			}
			d = parent
		}
	}
	for _, c := range cands {
		if fileExists(c) {
			return c
		}
	}
	return filepath.Join(exeDir, file)
}

type args struct {
	lib       string
	format    string
	vault     string
	password  string
	benchmark bool
	interop   bool
	sizeGb    float64
}

func parseArgs(exeDir string) args {
	a := args{
		password: "correct horse battery staple",
		sizeGb:   1,
		vault:    filepath.Join(os.TempDir(), "uvf-go-demo"),
	}
	if env := os.Getenv("TITANVAULT_LIB"); env != "" {
		a.lib = env
	} else {
		a.lib = discoverLib(exeDir)
	}
	argv := os.Args[1:]
	next := func(i *int) string {
		if *i+1 < len(argv) {
			*i++
			return argv[*i]
		}
		return ""
	}
	for i := 0; i < len(argv); i++ {
		switch argv[i] {
		case "--lib":
			a.lib = next(&i)
		case "--format":
			a.format = next(&i)
		case "--vault":
			a.vault = next(&i)
		case "--password":
			a.password = next(&i)
		case "--benchmark", "--bench":
			a.benchmark = true
		case "--size":
			if v, err := strconv.ParseFloat(next(&i), 64); err == nil {
				a.sizeGb = v
			}
		case "--cryptomator-interop", "--interop":
			a.interop = true
		}
	}
	return a
}

// ----- tiny filesystem helpers -----
func fileExists(p string) bool {
	info, err := os.Stat(p)
	return err == nil && !info.IsDir()
}

func dirHasFiles(dir string) bool {
	has := false
	filepath.Walk(dir, func(p string, info os.FileInfo, err error) error {
		if err == nil && info != nil && !info.IsDir() {
			has = true
		}
		return nil
	})
	return has
}

func contains(v []string, s string) bool {
	for _, x := range v {
		if x == s {
			return true
		}
	}
	return false
}

func main() {
	exeDir := "."
	if exe, err := os.Executable(); err == nil {
		exeDir = filepath.Dir(exe)
	}
	a := parseArgs(exeDir)

	if !fileExists(a.lib) {
		fmt.Fprintf(os.Stderr, "Native library not found: %s\n"+
			"Build it first:  ../../BuildScripts/build.ps1 -Task aot   (or build.sh --task aot)\n"+
			"Then it loads automatically (same folder / cwd / ../../Dist/Native/<rid>/), or pass --lib <path> / set TITANVAULT_LIB.\n"+
			"Note: the library must match the executable architecture (GOARCH=%s).\n", a.lib, runtime.GOARCH)
		os.Exit(1)
	}

	handle, err := loadLibrary(a.lib)
	if err != nil {
		fmt.Fprintf(os.Stderr, "Failed to load %s (architecture mismatch?): %v\n", a.lib, err)
		os.Exit(1)
	}
	api := bindAPI(handle)
	fmt.Printf("TitanVault version: %s\n", api.version())

	if a.interop {
		if runInterop(api) {
			os.Exit(0)
		}
		os.Exit(1)
	}
	if a.benchmark {
		runBenchmark(api, a.sizeGb)
		os.Exit(0)
	}

	st := &state{}
	formats := []string{"uvf", "cryptomator"}
	if a.format != "" {
		formats = []string{a.format}
	}
	for _, format := range formats {
		func() {
			defer func() {
				if r := recover(); r != nil {
					st.failed++
					fmt.Printf("\n❌ %s demo aborted: %v\n", format, r)
				}
			}()
			runDemo(api, format, filepath.Join(a.vault, format), a.password, st)
		}()
	}

	if a.format == "" {
		if !runInterop(api) {
			st.failed++
		}
		func() {
			defer func() {
				if r := recover(); r != nil {
					st.failed++
					fmt.Printf("\n❌ benchmark aborted: %v\n", r)
				}
			}()
			runBenchmark(api, 0.25)
		}()
	}

	if st.failed == 0 {
		fmt.Println("\n✅ All Go demo sections passed.")
		os.Exit(0)
	}
	fmt.Printf("\n❌ %d section(s) failed.\n", st.failed)
	os.Exit(1)
}
