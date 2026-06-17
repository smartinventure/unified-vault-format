// UVF / Cryptomator demo in Node.js via the native TitanVault library (C ABI, using `koffi`).
//
// Build the native library first (from the repo root):
//   dotnet publish Uvf.Net/UvfLib.Master/UvfLib.Master.csproj -c Release -r win-x64 -p:PublishAot=true
//   # produces TitanVault.dll (win) / libTitanVault.so (linux) / libTitanVault.dylib (macOS)
//
// Install + run:
//   npm install
//   node vault-demo.js --lib /path/to/TitanVault.dll --format uvf

const koffi = require('koffi');
const fs = require('fs');
const os = require('os');
const path = require('path');

const TITAN_VAULT_SUCCESS = 0;

function parseArgs() {
  const a = { lib: process.env.TITANVAULT_LIB || './TitanVault.dll', format: 'uvf',
              vault: path.join(os.tmpdir(), 'uvf-node-demo'), password: 'correct horse battery staple' };
  const argv = process.argv.slice(2);
  for (let i = 0; i < argv.length; i++) {
    const v = argv[i + 1];
    if (argv[i] === '--lib') { a.lib = v; i++; }
    else if (argv[i] === '--format') { a.format = v; i++; }
    else if (argv[i] === '--vault') { a.vault = v; i++; }
    else if (argv[i] === '--password') { a.password = v; i++; }
  }
  return a;
}

function load(libPath) {
  const lib = koffi.load(libPath);
  return {
    // Returned as char* — for a one-shot demo we let koffi decode it; production should return void*
    // and release it with titan_vault_free_string.
    getVersion: lib.func('titan_vault_get_version', 'char*', []),
    // Static buffer — must NOT be freed.
    getLastError: lib.func('titan_vault_get_last_error', 'char*', []),
    createUvf: lib.func('titan_vault_create_uvf_vault', 'int',
      ['char*', 'int', 'char*', 'int', 'int', 'int', 'int']),
    loadUvf: lib.func('titan_vault_load_uvf_vault', 'void*',
      ['char*', 'int', 'char*', 'int', 'char*', 'int']),
    createCryptomator: lib.func('titan_vault_create_cryptomator_vault', 'int',
      ['char*', 'int', 'char*', 'int']),
    loadCryptomator: lib.func('titan_vault_load_cryptomator_vault', 'void*',
      ['char*', 'int', 'char*', 'int']),
    writeFile: lib.func('titan_vault_write_file', 'int',
      ['void*', 'char*', 'int', 'void*', 'int']),
    // The in/out buffer-size parameter is marked _Inout_ so koffi writes the actual size back.
    readFile: lib.func('titan_vault_read_file', 'int',
      ['void*', 'char*', 'int', 'void*', '_Inout_ int*']),
    fileExists: lib.func('titan_vault_file_exists', 'int', ['void*', 'char*', 'int']),
    deleteFile: lib.func('titan_vault_delete_file', 'int', ['void*', 'char*', 'int']),
    closeVault: lib.func('titan_vault_close_vault', 'int', ['void*']),
  };
}

const u8len = (s) => Buffer.byteLength(s, 'utf8');

function check(lib, rc, what) {
  if (rc !== TITAN_VAULT_SUCCESS) throw new Error(`${what} failed (rc=${rc}): ${lib.getLastError()}`);
}

function main() {
  const args = parseArgs();
  const lib = load(args.lib);

  console.log(`TitanVault version: ${lib.getVersion()}`);
  fs.mkdirSync(args.vault, { recursive: true });

  const vlen = u8len(args.vault);
  const plen = u8len(args.password);

  // 1. Create + open the vault.
  let handle;
  if (args.format === 'uvf') {
    check(lib, lib.createUvf(args.vault, vlen, args.password, plen, 1, 0, 0), 'create_uvf_vault');
    handle = lib.loadUvf(args.vault, vlen, args.password, plen, null, 0);
  } else {
    check(lib, lib.createCryptomator(args.vault, vlen, args.password, plen), 'create_cryptomator_vault');
    handle = lib.loadCryptomator(args.vault, vlen, args.password, plen);
  }
  if (!handle) throw new Error(`load vault failed: ${lib.getLastError()}`);
  console.log(`Created + opened ${args.format} vault at ${args.vault}`);

  try {
    const filePath = '/hello.txt';
    const fpLen = u8len(filePath);
    const plaintext = Buffer.from('Hello, encrypted world!', 'utf8');

    // 2. Encrypt: write a file.
    check(lib, lib.writeFile(handle, filePath, fpLen, plaintext, plaintext.length), 'write_file');
    console.log(`Wrote ${filePath} (${plaintext.length} bytes)`);

    // 3. Decrypt: read it back (in/out size).
    const buf = Buffer.alloc(64 * 1024);
    const size = [buf.length];
    check(lib, lib.readFile(handle, filePath, fpLen, buf, size), 'read_file');
    const data = buf.subarray(0, size[0]);
    console.log(`Read back (decrypted): ${JSON.stringify(data.toString('utf8'))}`);
    if (!data.equals(plaintext)) throw new Error('round-trip mismatch!');

    // 4. Cleartext name is not on disk.
    const leaked = walk(args.vault).some((f) => path.basename(f) === 'hello.txt');
    console.log(`Backend stores plaintext name 'hello.txt'? ${leaked} (expected: false)`);

    // 5. Exists + delete.
    console.log(`file_exists(${filePath}) = ${lib.fileExists(handle, filePath, fpLen)} (1 == yes)`);
    check(lib, lib.deleteFile(handle, filePath, fpLen), 'delete_file');
    console.log(`after delete, file_exists = ${lib.fileExists(handle, filePath, fpLen)} (0 == no)`);
  } finally {
    lib.closeVault(handle);
  }
  console.log('✅ Node.js demo completed.');
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
