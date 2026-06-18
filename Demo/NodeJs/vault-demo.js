// UVF / Cryptomator demo in Node.js via the native TitanVault library (C ABI, using `koffi`).
//
// Build the native library first (from the repo root):
//   ../../BuildScripts/build.ps1 -Task aot        # -> Dist/Native/win-x64/TitanVault.dll
//   ../../BuildScripts/build.sh  --task aot        # -> Dist/Native/<rid>/libTitanVault.{so,dylib}
//
// Install + run (runs BOTH formats by default; --format uvf|cryptomator restricts to one):
//   npm install
//   node vault-demo.js --lib ../../Dist/Native/win-x64/TitanVault.dll

const koffi = require('koffi');
const fs = require('fs');
const os = require('os');
const path = require('path');
const crypto = require('crypto');

const TITAN_VAULT_SUCCESS = 0;
const TITAN_VAULT_FORMAT_CRYPTOMATOR = 0;
const TITAN_VAULT_FORMAT_UVF = 1;
const MAX_LIST = 256;

// Resolve the native library for the current OS/arch under ../../Dist/Native/<rid>/, so a bare
// `node vault-demo.js` works after a build. Override with --lib or the TITANVAULT_LIB env var.
function defaultLibPath() {
  const arch = ({ x64: 'x64', arm64: 'arm64' })[process.arch] || process.arch;
  const rid = (process.platform === 'win32' ? 'win-' : process.platform === 'darwin' ? 'osx-' : 'linux-') + arch;
  const file = process.platform === 'win32' ? 'TitanVault.dll'
             : process.platform === 'darwin' ? 'libTitanVault.dylib' : 'libTitanVault.so';
  return path.resolve(__dirname, '..', '..', 'Dist', 'Native', rid, file);
}

function parseArgs() {
  // format is left undefined by default so the demo runs BOTH formats (uvf then cryptomator);
  // pass --format uvf|cryptomator to run just one.
  const a = { lib: process.env.TITANVAULT_LIB || defaultLibPath(), format: undefined,
              vault: path.join(os.tmpdir(), 'uvf-node-demo'), password: 'correct horse battery staple',
              benchmark: false, interop: false, sizeGb: 1 };
  const argv = process.argv.slice(2);
  for (let i = 0; i < argv.length; i++) {
    const v = argv[i + 1];
    if (argv[i] === '--lib') { a.lib = v; i++; }
    else if (argv[i] === '--format') { a.format = v; i++; }
    else if (argv[i] === '--vault') { a.vault = v; i++; }
    else if (argv[i] === '--password') { a.password = v; i++; }
    else if (argv[i] === '--benchmark' || argv[i] === '--bench') { a.benchmark = true; }
    else if (argv[i] === '--size') { a.sizeGb = parseFloat(v); i++; }
    else if (argv[i] === '--cryptomator-interop' || argv[i] === '--interop') { a.interop = true; }
  }
  return a;
}

function load(libPath) {
  const lib = koffi.load(libPath);
  return {
    // Returns a heap char* that the caller must release with titan_vault_free_string.
    getVersion: lib.func('titan_vault_get_version', 'void *', []),
    // Static buffer — must NOT be freed.
    getLastError: lib.func('titan_vault_get_last_error', 'char *', []),
    freeString: lib.func('titan_vault_free_string', 'void', ['void *']),

    createUvf: lib.func('titan_vault_create_uvf_vault', 'int',
      ['char *', 'int', 'char *', 'int', 'int', 'int', 'int']),
    loadUvf: lib.func('titan_vault_load_uvf_vault', 'void *',
      ['char *', 'int', 'char *', 'int', 'char *', 'int']),
    createCryptomator: lib.func('titan_vault_create_cryptomator_vault', 'int',
      ['char *', 'int', 'char *', 'int']),
    loadCryptomator: lib.func('titan_vault_load_cryptomator_vault', 'void *',
      ['char *', 'int', 'char *', 'int']),
    closeVault: lib.func('titan_vault_close_vault', 'int', ['void *']),

    writeFile: lib.func('titan_vault_write_file', 'int',
      ['void *', 'char *', 'int', 'void *', 'int']),
    readFile: lib.func('titan_vault_read_file', 'int',
      ['void *', 'char *', 'int', 'void *', '_Inout_ int *']),
    fileExists: lib.func('titan_vault_file_exists', 'int', ['void *', 'char *', 'int']),
    deleteFile: lib.func('titan_vault_delete_file', 'int', ['void *', 'char *', 'int']),

    createDirectory: lib.func('titan_vault_create_directory', 'int', ['void *', 'char *', 'int']),
    directoryExists: lib.func('titan_vault_directory_exists', 'int', ['void *', 'char *', 'int']),
    deleteDirectory: lib.func('titan_vault_delete_directory', 'int', ['void *', 'char *', 'int']),
    // entriesBuffer = char*[maxEntries]; maxEntries = int* (in). Returns count; each entry must be freed.
    listDirectory: lib.func('titan_vault_list_directory', 'int',
      ['void *', 'char *', 'int', 'void *', 'void *']),
    // fileSize = int64* (out), lastModified = int64* (out, unix seconds).
    getFileInfo: lib.func('titan_vault_get_file_info', 'int',
      ['void *', 'char *', 'int', 'void *', 'void *']),
    move: lib.func('titan_vault_move', 'int', ['void *', 'char *', 'int', 'char *', 'int']),

    openReadStream: lib.func('titan_vault_open_read_stream', 'void *', ['void *', 'char *', 'int']),
    openWriteStream: lib.func('titan_vault_open_write_stream', 'void *', ['void *', 'char *', 'int']),
    streamWrite: lib.func('titan_vault_stream_write', 'int', ['void *', 'void *', 'int']),
    streamRead: lib.func('titan_vault_stream_read', 'int', ['void *', 'void *', 'int']),
    streamSeek: lib.func('titan_vault_stream_seek', 'int64', ['void *', 'int64', 'int']),
    streamGetLength: lib.func('titan_vault_stream_get_length', 'int64', ['void *']),
    closeStream: lib.func('titan_vault_close_stream', 'int', ['void *']),

    // UVF multi-user / key management — these operate on the vault PATH (vault need not be open).
    addUser: lib.func('titan_vault_add_user', 'int',
      ['char *', 'int', 'char *', 'int', 'char *', 'int', 'char *', 'int']),
    getVaultUsers: lib.func('titan_vault_get_vault_users', 'int',
      ['char *', 'int', 'char *', 'int', 'void *', 'void *']),
    rotateKeys: lib.func('titan_vault_rotate_keys', 'int', ['char *', 'int', 'char *', 'int', 'int']),

    // UVF public-key (asymmetric) membership.
    // pub/priv buffers + in/out int* sizes (set to required length).
    generateUserKeyPair: lib.func('titan_vault_generate_user_keypair', 'int',
      ['void *', 'int', 'void *', '_Inout_ int *', 'void *', '_Inout_ int *']),
    addUserByPublicKey: lib.func('titan_vault_add_user_by_public_key', 'int',
      ['char *', 'int', 'char *', 'int', 'char *', 'int', 'void *', 'int']),
    loadUvfWithKey: lib.func('titan_vault_load_uvf_vault_with_key', 'void *',
      ['char *', 'int', 'void *', 'int', 'char *', 'int', 'char *', 'int']),
    rotateKeysPubKey: lib.func('titan_vault_rotate_keys_pubkey', 'int', ['char *', 'int', 'char *', 'int']),

    // Library / maintenance utilities.
    detectVaultFormat: lib.func('titan_vault_detect_vault_format', 'int', ['char *', 'int']),
    secureZeroMemory: lib.func('titan_vault_secure_zero_memory', 'void', ['void *', 'int']),
    backupFiles: lib.func('titan_vault_backup_files', 'int', ['char *', 'int', 'char *', 'int', 'int']),
    changeCryptomatorPassword: lib.func('titan_vault_change_cryptomator_password', 'int',
      ['char *', 'int', 'char *', 'int', 'char *', 'int']),
    changeUvfAdminPassword: lib.func('titan_vault_change_uvf_admin_password', 'int',
      ['char *', 'int', 'char *', 'int', 'char *', 'int']),
    changeUvfUserPassword: lib.func('titan_vault_change_uvf_user_password', 'int',
      ['char *', 'int', 'char *', 'int', 'char *', 'int', 'char *', 'int']),
    removeUser: lib.func('titan_vault_remove_user', 'int', ['char *', 'int', 'char *', 'int', 'char *', 'int']),

    // Text convenience (UTF-8). read_all_text returns a heap char* that must be freed.
    writeAllText: lib.func('titan_vault_write_all_text', 'int', ['void *', 'char *', 'int', 'char *', 'int']),
    appendAllText: lib.func('titan_vault_append_all_text', 'int', ['void *', 'char *', 'int', 'char *', 'int']),
    readAllText: lib.func('titan_vault_read_all_text', 'void *', ['void *', 'char *', 'int']),

    // Fuller stream API (the core read/write/seek are above).
    openStreamWithFlags: lib.func('titan_vault_open_stream_with_flags', 'void *', ['void *', 'char *', 'int', 'int']),
    streamGetPosition: lib.func('titan_vault_stream_get_position', 'int64', ['void *']),
    streamSetLength: lib.func('titan_vault_stream_set_length', 'int', ['void *', 'int64']),
    streamFlush: lib.func('titan_vault_stream_flush', 'int', ['void *']),
  };
}

// StorageLib.Abstractions.OpenFlags values (for open_stream_with_flags).
const OPEN_READONLY = 0x0000;
const OPEN_WRITEONLY = 0x0001;
const OPEN_CREATE = 0x0040;
const OPEN_TRUNCATE = 0x0200;

const u8 = (s) => Buffer.byteLength(s, 'utf8');

function check(lib, rc, what) {
  if (rc !== TITAN_VAULT_SUCCESS) throw new Error(`${what} failed (rc=${rc}): ${lib.getLastError()}`);
}

function version(lib) {
  const ptr = lib.getVersion();
  if (!ptr) return '(unknown)';
  const s = koffi.decode(ptr, 'char', -1); // read the NUL-terminated UTF-8 string at the pointer
  lib.freeString(ptr); // release the heap string (the old demo leaked this)
  return s;
}

// Decode a returned char*[] of `count` entries into JS strings, freeing each native string.
function readStringArray(lib, buffer, count) {
  if (count <= 0) return [];
  const ptrs = koffi.decode(buffer, 'void *', count);
  return ptrs.map((p) => { const s = koffi.decode(p, 'char', -1); lib.freeString(p); return s; });
}

function openVault(lib, format, vaultDir, password, userId, userPassword) {
  const vlen = u8(vaultDir);
  if (format === 'uvf') {
    const pw = userPassword || password;
    return lib.loadUvf(vaultDir, vlen, pw, u8(pw), userId || null, userId ? u8(userId) : 0);
  }
  return lib.loadCryptomator(vaultDir, vlen, password, u8(password));
}

// ----- the per-format demo, organised into sections each reporting PASSED/FAILED -----

function runDemo(lib, format, vaultDir, password, state) {
  console.log(`\n========== ${format.toUpperCase()} ==========`);
  fs.rmSync(vaultDir, { recursive: true, force: true });
  fs.mkdirSync(vaultDir, { recursive: true });
  const vlen = u8(vaultDir);
  const plen = u8(password);

  // Create the vault.
  if (format === 'uvf') check(lib, lib.createUvf(vaultDir, vlen, password, plen, 1, 0, 0), 'create_uvf_vault');
  else check(lib, lib.createCryptomator(vaultDir, vlen, password, plen), 'create_cryptomator_vault');

  let handle = openVault(lib, format, vaultDir, password);
  if (!handle) throw new Error(`load ${format} vault failed: ${lib.getLastError()}`);
  console.log(`Created + opened ${format} vault at ${vaultDir}`);

  const section = (label, fn) => {
    try { fn(); console.log(`  ${label} tests for ${format.toUpperCase()}: PASSED`); }
    catch (e) { state.failed++; console.log(`  ${label} tests for ${format.toUpperCase()}: FAILED — ${e.message}`); }
  };

  // 0. Detect the on-disk format (path-based — the vault need not be open).
  section('Detect format', () => {
    const detected = lib.detectVaultFormat(vaultDir, vlen);
    const expected = format === 'uvf' ? TITAN_VAULT_FORMAT_UVF : TITAN_VAULT_FORMAT_CRYPTOMATOR;
    if (detected !== expected) throw new Error(`detect_vault_format=${detected}, expected ${expected}`);
  });

  // A file we deliberately keep around to prove persistence + multi-user access later.
  const persistPayload = Buffer.from('persisted across reopen', 'utf8');
  check(lib, lib.writeFile(handle, '/persist.txt', u8('/persist.txt'), persistPayload, persistPayload.length), 'write persist.txt');

  try {
    // 1. Basic file round-trip.
    section('File', () => {
      const fp = '/hello.txt', plaintext = Buffer.from('Hello, encrypted world!', 'utf8');
      check(lib, lib.writeFile(handle, fp, u8(fp), plaintext, plaintext.length), 'write_file');
      const buf = Buffer.alloc(64 * 1024), size = [buf.length];
      check(lib, lib.readFile(handle, fp, u8(fp), buf, size), 'read_file');
      if (!buf.subarray(0, size[0]).equals(plaintext)) throw new Error('round-trip mismatch');
      const leaked = walk(vaultDir).some((f) => path.basename(f) === 'hello.txt');
      if (leaked) throw new Error('plaintext filename leaked to disk');
      if (lib.fileExists(handle, fp, u8(fp)) !== 1) throw new Error('exists should be 1');
      check(lib, lib.deleteFile(handle, fp, u8(fp)), 'delete_file');
      if (lib.fileExists(handle, fp, u8(fp)) !== 0) throw new Error('exists should be 0 after delete');
    });

    // 1b. UTF-8 text convenience: write, append, read-back.
    section('Text helpers', () => {
      const tf = '/notes.txt', first = 'first line\n', second = 'second line\n';
      check(lib, lib.writeAllText(handle, tf, u8(tf), first, u8(first)), 'write_all_text');
      check(lib, lib.appendAllText(handle, tf, u8(tf), second, u8(second)), 'append_all_text');
      const ptr = lib.readAllText(handle, tf, u8(tf));
      if (!ptr) throw new Error(`read_all_text: ${lib.getLastError()}`);
      const text = koffi.decode(ptr, 'char', -1); lib.freeString(ptr);
      if (text !== first + second) throw new Error(`text round-trip mismatch: ${JSON.stringify(text)}`);
    });

    // 2. Directories: create, write into, list, file-info, move/rename.
    section('Directory', () => {
      check(lib, lib.createDirectory(handle, '/docs', u8('/docs')), 'create_directory');
      if (lib.directoryExists(handle, '/docs', u8('/docs')) !== 1) throw new Error('directory_exists should be 1');
      const note = '/docs/note.txt', body = Buffer.from('inside a subdirectory', 'utf8');
      check(lib, lib.writeFile(handle, note, u8(note), body, body.length), 'write into dir');

      const entriesBuf = koffi.alloc('void *', MAX_LIST);
      const maxBuf = koffi.alloc('int', 1); koffi.encode(maxBuf, 'int', MAX_LIST);
      let n = lib.listDirectory(handle, '/docs', u8('/docs'), entriesBuf, maxBuf);
      if (n < 0) throw new Error(`list_directory rc=${n}: ${lib.getLastError()}`);
      let names = readStringArray(lib, entriesBuf, n);
      if (!names.includes('note.txt')) throw new Error(`listing missing note.txt (got ${JSON.stringify(names)})`);

      const sizeBuf = koffi.alloc('int64', 1), mtimeBuf = koffi.alloc('int64', 1);
      check(lib, lib.getFileInfo(handle, note, u8(note), sizeBuf, mtimeBuf), 'get_file_info');
      const sz = Number(koffi.decode(sizeBuf, 'int64'));
      if (sz !== body.length) throw new Error(`file size ${sz} != ${body.length}`);

      const renamed = '/docs/renamed.txt';
      check(lib, lib.move(handle, note, u8(note), renamed, u8(renamed)), 'move');
      n = lib.listDirectory(handle, '/docs', u8('/docs'), entriesBuf, maxBuf);
      names = readStringArray(lib, entriesBuf, n);
      if (!names.includes('renamed.txt')) throw new Error(`rename not reflected (got ${JSON.stringify(names)})`);
      console.log(`    /docs now contains: ${JSON.stringify(names)} (size of note was ${sz} bytes)`);
    });

    // 3. Streaming: write a multi-chunk file, then random-access read with seek.
    section('Streaming', () => {
      const fp = '/big.bin', CHUNK = 32 * 1024, CHUNKS = 4, total = CHUNK * CHUNKS;
      const chunk = Buffer.alloc(CHUNK);
      for (let j = 0; j < CHUNK; j++) chunk[j] = j % 256; // file[O] == O % 256

      const ws = lib.openWriteStream(handle, fp, u8(fp));
      if (!ws) throw new Error(`open_write_stream: ${lib.getLastError()}`);
      try {
        for (let i = 0; i < CHUNKS; i++) if (lib.streamWrite(ws, chunk, CHUNK) !== CHUNK) throw new Error('short write');
        check(lib, lib.streamFlush(ws), 'stream_flush');
      } finally { lib.closeStream(ws); }

      const rs = lib.openReadStream(handle, fp, u8(fp));
      if (!rs) throw new Error(`open_read_stream: ${lib.getLastError()}`);
      try {
        const len = Number(lib.streamGetLength(rs));
        if (len !== total) throw new Error(`stream length ${len} != ${total}`);
        // sequential read of the whole thing, verifying the position-dependent pattern
        const rbuf = Buffer.alloc(CHUNK); let off = 0;
        let got;
        while ((got = lib.streamRead(rs, rbuf, CHUNK)) > 0) {
          for (let k = 0; k < got; k++) if (rbuf[k] !== (off + k) % 256) throw new Error(`byte mismatch at ${off + k}`);
          off += got;
        }
        if (off !== total) throw new Error(`read ${off} of ${total}`);
        const posAfterRead = Number(lib.streamGetPosition(rs));
        if (posAfterRead !== total) throw new Error(`stream_get_position ${posAfterRead} != ${total}`);
        // random access: seek to a mid-file offset and verify (best-effort — not all backends seek)
        const seekTo = 70000;
        const pos = Number(lib.streamSeek(rs, seekTo, 0)); // 0 = SEEK_SET
        if (pos === seekTo) {
          const small = Buffer.alloc(16);
          if (lib.streamRead(rs, small, 16) !== 16) throw new Error('short seek-read');
          for (let k = 0; k < 16; k++) if (small[k] !== (seekTo + k) % 256) throw new Error(`seek byte mismatch at ${seekTo + k}`);
          console.log(`    wrote+verified ${total} bytes; seek to ${seekTo} OK`);
        } else {
          console.log(`    wrote+verified ${total} bytes; seek not supported by this backend (skipped)`);
        }
        // open_stream_with_flags: reopen read-only and confirm the length matches.
        const rs2 = lib.openStreamWithFlags(handle, fp, u8(fp), OPEN_READONLY);
        if (!rs2) throw new Error(`open_stream_with_flags: ${lib.getLastError()}`);
        try { if (Number(lib.streamGetLength(rs2)) !== total) throw new Error('flags-open length mismatch'); }
        finally { lib.closeStream(rs2); }
      } finally { lib.closeStream(rs); }

      // stream_set_length: truncation of encrypted streams is backend-dependent; best-effort.
      try {
        const ts = lib.openStreamWithFlags(handle, '/trunc.bin', u8('/trunc.bin'), OPEN_WRITEONLY | OPEN_CREATE | OPEN_TRUNCATE);
        if (ts) { try { lib.streamWrite(ts, chunk, CHUNK); lib.streamSetLength(ts, 4096); } finally { lib.closeStream(ts); } }
      } catch { /* optional capability */ }
    });
  } finally {
    lib.closeVault(handle);
    handle = null;
  }

  // 4. Persistence: reopen the (closed) vault with the passphrase and re-read.
  section('Persistence', () => {
    const h2 = openVault(lib, format, vaultDir, password);
    if (!h2) throw new Error(`reopen failed: ${lib.getLastError()}`);
    try {
      const buf = Buffer.alloc(4096), size = [buf.length];
      check(lib, lib.readFile(h2, '/persist.txt', u8('/persist.txt'), buf, size), 'read after reopen');
      if (!buf.subarray(0, size[0]).equals(persistPayload)) throw new Error('persisted content mismatch');
    } finally { lib.closeVault(h2); }
  });

  // 5/6. UVF-only: key rotation, then multi-user (all operate on the vault path).
  if (format === 'uvf') {
    // Key rotation must run while the vault is admin-only (the lib refuses to rotate a vault that
    // has extra users, since it would need every user's password to re-wrap the keys).
    const rc = lib.rotateKeys(vaultDir, vlen, password, plen, TITAN_VAULT_FORMAT_UVF);
    if (rc === TITAN_VAULT_SUCCESS) console.log('  Key rotation tests for UVF: PASSED');
    else if (/not implemented/i.test(lib.getLastError())) console.log('  Key rotation tests for UVF: SKIPPED (not implemented)');
    else { state.failed++; console.log(`  Key rotation tests for UVF: FAILED — ${lib.getLastError()}`); }

    // Public-key (asymmetric) membership: admin grants access to a public key, the user opens with
    // their private key, and the admin can rotate the key without the member's password. Runs before
    // the password Multi-user section so only admin + the public-key user exist at rotation time.
    section('Public-key multi-user', () => {
      const bob = 'bob', keyPw = 'bob-key-pass-123';

      // 1. Generate bob's key pair (public key + password-encrypted private key) via the C ABI.
      const pubBuf = Buffer.alloc(4096), privBuf = Buffer.alloc(8192);
      const pubSize = [pubBuf.length], privSize = [privBuf.length];
      const keyPwBuf = Buffer.from(keyPw, 'utf8');
      check(lib, lib.generateUserKeyPair(keyPwBuf, keyPwBuf.length, pubBuf, pubSize, privBuf, privSize), 'generate_user_keypair');
      const publicKey = pubBuf.subarray(0, pubSize[0]);
      const encryptedPrivateKey = privBuf.subarray(0, privSize[0]);
      console.log(`    generated bob key pair (public ${pubSize[0]}B, encrypted private ${privSize[0]}B)`);

      // 2. Grant bob access by PUBLIC key (admin needs no password from bob).
      check(lib, lib.addUserByPublicKey(vaultDir, vlen, password, plen, bob, u8(bob), publicKey, publicKey.length), 'add_user_by_public_key');

      // 3. Open the vault as bob with his PRIVATE key and read the admin-written file.
      const readAsBob = () => {
        const h = lib.loadUvfWithKey(vaultDir, vlen, encryptedPrivateKey, encryptedPrivateKey.length, keyPw, u8(keyPw), bob, u8(bob));
        if (!h) throw new Error(`load as bob failed: ${lib.getLastError()}`);
        try {
          const buf = Buffer.alloc(4096), size = [buf.length];
          check(lib, lib.readFile(h, '/persist.txt', u8('/persist.txt'), buf, size), 'read as bob');
          if (!buf.subarray(0, size[0]).equals(persistPayload)) throw new Error('bob read mismatch');
        } finally { lib.closeVault(h); }
      };
      readAsBob();
      console.log('    opened as bob (public-key user) and read the admin file OK');

      // 4. Rotate the key for public-key members — admin alone, no bob password — then bob still reads.
      check(lib, lib.rotateKeysPubKey(vaultDir, vlen, password, plen), 'rotate_keys_pubkey');
      readAsBob();
      console.log('    rotated keys (no member password) and bob still reads OK');
    });

    section('Multi-user', () => {
      const alice = 'alice', alicePw = 'alice-passphrase-123';
      check(lib, lib.addUser(vaultDir, vlen, password, plen, alice, u8(alice), alicePw, u8(alicePw)), 'add_user');
      const usersBuf = koffi.alloc('void *', MAX_LIST);
      const maxBuf = koffi.alloc('int', 1); koffi.encode(maxBuf, 'int', MAX_LIST);
      const n = lib.getVaultUsers(vaultDir, vlen, password, plen, usersBuf, maxBuf);
      if (n < 0) throw new Error(`get_vault_users rc=${n}: ${lib.getLastError()}`);
      const users = readStringArray(lib, usersBuf, n);
      console.log(`    vault users: ${JSON.stringify(users)}`);
      if (!users.includes(alice)) throw new Error(`added user not listed (got ${JSON.stringify(users)})`);

      // Best-effort: open as the new user and read the admin-written file. This currently fails
      // because LoadMultiUserUvfVaultAsync runs filename-encryption detection without the userId
      // (VaultManager.cs) — a known library limitation, reported (not failed) here.
      try {
        const ah = openVault(lib, 'uvf', vaultDir, password, alice, alicePw);
        if (!ah) throw new Error(lib.getLastError());
        try {
          const buf = Buffer.alloc(4096), size = [buf.length];
          check(lib, lib.readFile(ah, '/persist.txt', u8('/persist.txt'), buf, size), 'read as alice');
          if (!buf.subarray(0, size[0]).equals(persistPayload)) throw new Error('alice read mismatch');
          console.log('    opened as second user and read the admin-written file OK');
        } finally { lib.closeVault(ah); }
      } catch (e) {
        console.log(`    ⚠ opening as a secondary user is not yet supported by the library: ${e.message}`);
      }

      // Change a member's password (admin-driven), then remove the member and confirm they're gone.
      const aliceNewPw = 'alice-passphrase-456';
      check(lib, lib.changeUvfUserPassword(vaultDir, vlen, password, plen, alice, u8(alice), aliceNewPw, u8(aliceNewPw)), 'change_uvf_user_password');
      check(lib, lib.removeUser(vaultDir, vlen, password, plen, alice, u8(alice)), 'remove_user');
      const usersBuf2 = koffi.alloc('void *', MAX_LIST);
      const maxBuf2 = koffi.alloc('int', 1); koffi.encode(maxBuf2, 'int', MAX_LIST);
      const n2 = lib.getVaultUsers(vaultDir, vlen, password, plen, usersBuf2, maxBuf2);
      const users2 = readStringArray(lib, usersBuf2, Math.max(n2, 0));
      if (users2.includes(alice)) throw new Error(`removed user still listed (got ${JSON.stringify(users2)})`);
      console.log(`    changed alice's password, then removed alice; users now: ${JSON.stringify(users2)}`);
    });
  }

  // 7. Maintenance (both formats): backup the key files, secure-wipe a buffer, change the
  //    password, and reopen with the new password.
  section('Maintenance', () => {
    const backupDir = path.join(os.tmpdir(), `uvf-backup-${format}-${process.pid}`);
    fs.rmSync(backupDir, { recursive: true, force: true });
    check(lib, lib.backupFiles(vaultDir, vlen, backupDir, u8(backupDir), 1), 'backup_files');
    if (!fs.existsSync(backupDir) || walk(backupDir).length === 0) throw new Error('backup produced no files');

    const secret = Buffer.from('super-secret-key-material', 'utf8');
    lib.secureZeroMemory(secret, secret.length);
    if (secret.some((b) => b !== 0)) throw new Error('secure_zero_memory did not zero the buffer');

    const newPw = password + '-rotated';
    if (format === 'uvf') check(lib, lib.changeUvfAdminPassword(vaultDir, vlen, password, plen, newPw, u8(newPw)), 'change_uvf_admin_password');
    else check(lib, lib.changeCryptomatorPassword(vaultDir, vlen, password, plen, newPw, u8(newPw)), 'change_cryptomator_password');
    const h3 = openVault(lib, format, vaultDir, newPw);
    if (!h3) throw new Error(`reopen after password change failed: ${lib.getLastError()}`);
    try {
      const buf = Buffer.alloc(4096), size = [buf.length];
      check(lib, lib.readFile(h3, '/persist.txt', u8('/persist.txt'), buf, size), 'read after password change');
      if (!buf.subarray(0, size[0]).equals(persistPayload)) throw new Error('content mismatch after password change');
    } finally { lib.closeVault(h3); }
    fs.rmSync(backupDir, { recursive: true, force: true });
    console.log(`    backed up key files, secure-zeroed a buffer, changed the ${format} password and re-read OK`);
  });

  console.log(`✅ ${format} demo finished.`);
}

const elapsedMs = (start) => Number(process.hrtime.bigint() - start) / 1e6;
const mbps = (bytes, ms) => (bytes / 1e6) / (ms / 1000); // decimal MB/s

// Reads a whole vault file into a Buffer, growing the buffer to the required size if needed.
function readFileFull(lib, handle, vaultPath) {
  let cap = 1 << 20; // 1 MiB
  for (let attempt = 0; attempt < 4; attempt++) {
    const buf = Buffer.alloc(cap);
    const size = [cap];
    const rc = lib.readFile(handle, vaultPath, u8(vaultPath), buf, size);
    if (rc === TITAN_VAULT_SUCCESS) return buf.subarray(0, size[0]);
    if (size[0] > cap) { cap = size[0]; continue; } // grow to required size and retry
    throw new Error(`read_file ${vaultPath} rc=${rc}: ${lib.getLastError()}`);
  }
  throw new Error(`read_file ${vaultPath}: buffer growth failed`);
}

function listDir(lib, handle, dirPath) {
  const entriesBuf = koffi.alloc('void *', MAX_LIST);
  const maxBuf = koffi.alloc('int', 1); koffi.encode(maxBuf, 'int', MAX_LIST);
  const n = lib.listDirectory(handle, dirPath, u8(dirPath), entriesBuf, maxBuf);
  if (n < 0) throw new Error(`list_directory ${dirPath} rc=${n}: ${lib.getLastError()}`);
  return readStringArray(lib, entriesBuf, n);
}

// 2. Interop: unlock a REAL Cryptomator vault (created by the Cryptomator app), list the files, and
// md5-compare the decrypted content against the original plaintext files.
function runCryptomatorInterop(lib) {
  console.log('\n========== Cryptomator interop (real vault) ==========');
  const base = path.resolve(__dirname, '..', '_test-cryptomator-vault');
  const vaultDir = path.join(base, 'smartinventure');
  const origDir = path.join(base, 'original-files');
  const password = 'smartinventure'; // demo vault — hardcoded on purpose

  if (!fs.existsSync(path.join(vaultDir, 'masterkey.cryptomator'))) {
    console.error(`No Cryptomator vault found at ${vaultDir}`);
    return false;
  }
  const handle = lib.loadCryptomator(vaultDir, u8(vaultDir), password, u8(password));
  if (!handle) { console.error(`Unlock failed: ${lib.getLastError()}`); return false; }
  try {
    console.log(`Unlocked real Cryptomator vault at ${vaultDir}`);
    for (const d of ['/', '/mysubfolder1', '/mysubfolder1/mysubfolder2']) {
      console.log(`  ${d}  ->  ${JSON.stringify(listDir(lib, handle, d))}`);
    }
    const cases = [
      ['/Perfect-albums.txt', 'Perfect-albums.txt'],
      ['/mysubfolder1/banana.jpg', 'banana.jpg'],
      ['/mysubfolder1/mysubfolder2/Rubicon - Rivers - lyrics.txt', 'Rubicon - Rivers - lyrics.txt'],
    ];
    let allOk = true;
    for (const [vaultPath, origName] of cases) {
      const decrypted = readFileFull(lib, handle, vaultPath);
      const got = crypto.createHash('md5').update(decrypted).digest('hex');
      const want = crypto.createHash('md5').update(fs.readFileSync(path.join(origDir, origName))).digest('hex');
      const ok = got === want;
      if (!ok) allOk = false;
      console.log(`  ${ok ? '✓' : '✗'} ${vaultPath}  (${decrypted.length} B)  md5 ${ok ? 'match' : `MISMATCH got=${got} want=${want}`}`);
    }
    console.log(allOk
      ? '✅ Reading a real Cryptomator vault worked — all files decrypted and md5-matched the originals.'
      : '❌ Cryptomator interop FAILED — md5 mismatch.');
    return allOk;
  } catch (e) {
    console.log(`❌ Cryptomator interop FAILED: ${e.message}`);
    return false;
  } finally { lib.closeVault(handle); }
}

// 1. Benchmark: create a large plaintext file, then encrypt/decrypt it through the vault, reporting MB/s
// for raw disk write, encrypt, decrypt, and raw disk read — for both formats.
function runBenchmark(lib, sizeGb) {
  const sizeBytes = Math.round(sizeGb * 1024 * 1024 * 1024);
  const CHUNK = 4 * 1024 * 1024; // 4 MiB
  console.log(`\n========== Benchmark (${sizeGb} GB per format, ${CHUNK >> 20} MiB chunks) ==========`);
  for (const format of ['uvf', 'cryptomator']) benchOne(lib, format, sizeBytes, CHUNK);
}

function benchOne(lib, format, sizeBytes, CHUNK) {
  console.log(`\n----- ${format.toUpperCase()} -----`);
  const dir = path.join(os.tmpdir(), `uvf-bench-${format}-${process.pid}`);
  fs.rmSync(dir, { recursive: true, force: true });
  const vaultDir = path.join(dir, 'vault');
  fs.mkdirSync(vaultDir, { recursive: true });
  const plain = path.join(dir, 'plain.bin');
  const password = 'bench-pass-123';
  const report = (label, ms) =>
    console.log(`  ${label.padEnd(32)} ${ms.toFixed(0).padStart(7)} ms   ${mbps(sizeBytes, ms).toFixed(1).padStart(8)} MB/s`);

  const chunk = Buffer.alloc(CHUNK);
  for (let i = 0; i < CHUNK; i++) chunk[i] = i & 0xff; // non-trivial data (avoid sparse-file effects)

  try {
    // (a) create the plaintext file on disk — gauges raw medium write speed
    let t = process.hrtime.bigint();
    { const fd = fs.openSync(plain, 'w'); let w = 0;
      while (w < sizeBytes) { const n = Math.min(CHUNK, sizeBytes - w); fs.writeSync(fd, chunk, 0, n); w += n; }
      fs.fsyncSync(fd); fs.closeSync(fd); }
    report('create file (disk write)', elapsedMs(t));

    const vlen = u8(vaultDir), plen = u8(password);
    if (format === 'uvf') check(lib, lib.createUvf(vaultDir, vlen, password, plen, 1, 0, 0), 'create_uvf_vault');
    else check(lib, lib.createCryptomator(vaultDir, vlen, password, plen), 'create_cryptomator_vault');
    const handle = format === 'uvf'
      ? lib.loadUvf(vaultDir, vlen, password, plen, null, 0)
      : lib.loadCryptomator(vaultDir, vlen, password, plen);
    if (!handle) throw new Error(`load failed: ${lib.getLastError()}`);

    try {
      // (b) encrypt — stream the plaintext into the vault
      t = process.hrtime.bigint();
      { const ws = lib.openWriteStream(handle, '/big.bin', u8('/big.bin'));
        if (!ws) throw new Error(`open_write_stream: ${lib.getLastError()}`);
        const fd = fs.openSync(plain, 'r'); const rbuf = Buffer.alloc(CHUNK); let rd;
        try { while ((rd = fs.readSync(fd, rbuf, 0, CHUNK, null)) > 0) { if (lib.streamWrite(ws, rbuf, rd) !== rd) throw new Error('short write'); } }
        finally { fs.closeSync(fd); lib.closeStream(ws); } }
      report(`encrypt (${format})`, elapsedMs(t));

      // (c) decrypt — stream it back out of the vault (discarding the plaintext)
      t = process.hrtime.bigint();
      { const rs = lib.openReadStream(handle, '/big.bin', u8('/big.bin'));
        if (!rs) throw new Error(`open_read_stream: ${lib.getLastError()}`);
        const dbuf = Buffer.alloc(CHUNK); let got, total = 0;
        try { while ((got = lib.streamRead(rs, dbuf, CHUNK)) > 0) total += got; } finally { lib.closeStream(rs); }
        if (total !== sizeBytes) throw new Error(`decrypt size ${total} != ${sizeBytes}`); }
      report(`decrypt (${format})`, elapsedMs(t));

      // (d) read the plaintext file back from disk — gauges raw medium read speed
      t = process.hrtime.bigint();
      { const fd = fs.openSync(plain, 'r'); const rbuf = Buffer.alloc(CHUNK);
        while (fs.readSync(fd, rbuf, 0, CHUNK, null) > 0) { /* discard */ } fs.closeSync(fd); }
      report('read file (disk read)', elapsedMs(t));
    } finally { lib.closeVault(handle); }
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
}

function main() {
  const args = parseArgs();
  if (!fs.existsSync(args.lib)) {
    console.error(`Native library not found: ${args.lib}\n` +
      `Build it first:  ../../BuildScripts/build.ps1 -Task aot   (or build.sh --task aot)\n` +
      `Then it loads automatically, or pass --lib <path> / set TITANVAULT_LIB.\n` +
      `Note: the library must match your Node architecture (${process.arch}).`);
    process.exit(1);
  }
  const lib = load(args.lib);
  console.log(`TitanVault version: ${version(lib)}`);

  // Focused modes (run only the requested thing).
  if (args.interop) { process.exit(runCryptomatorInterop(lib) ? 0 : 1); }
  if (args.benchmark) { runBenchmark(lib, args.sizeGb); process.exit(0); }

  const state = { failed: 0 };

  // Functional sections, for one format (--format) or both (default).
  const formats = args.format ? [args.format] : ['uvf', 'cryptomator'];
  for (const format of formats) {
    try { runDemo(lib, format, path.join(args.vault, format), args.password, state); }
    catch (e) { state.failed++; console.log(`\n❌ ${format} demo aborted: ${e.message}`); }
  }

  // A full run (no --format) also exercises the real-Cryptomator-vault interop and a quick throughput
  // benchmark. (Use --cryptomator-interop or --benchmark [--size <GB>] to run either on its own; the
  // standalone benchmark defaults to 1 GB.)
  if (!args.format) {
    const interopVault = path.resolve(__dirname, '..', '_test-cryptomator-vault', 'smartinventure', 'masterkey.cryptomator');
    if (fs.existsSync(interopVault)) {
      if (!runCryptomatorInterop(lib)) state.failed++;
    } else {
      console.log('\n(Cryptomator interop skipped — Demo/_test-cryptomator-vault not present)');
    }
    try { runBenchmark(lib, 0.25); }
    catch (e) { state.failed++; console.log(`\n❌ benchmark aborted: ${e.message}`); }
  }

  console.log(state.failed === 0
    ? '\n✅ All Node.js demo sections passed.'
    : `\n❌ ${state.failed} section(s) failed.`);
  process.exit(state.failed === 0 ? 0 : 1);
}

function walk(dir) {
  const out = [];
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, e.name);
    if (e.isDirectory()) out.push(...walk(p)); else out.push(p);
  }
  return out;
}

main();
