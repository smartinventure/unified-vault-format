// Creates a Cryptomator vault (via the native TitanVault library) with the SAME layout and files as
// Demo/_test-cryptomator-vault, so it can be opened in the REAL Cryptomator app to verify that vaults
// WE write are compatible (the reverse of the read-interop test).
//
//   node generate-cryptomator-vault.js <vault-directory>
//
// Password is hardcoded to "smartinventure" (demo only). File contents are copied from
// Demo/_test-cryptomator-vault/original-files/ so they md5-match the read-interop test's originals.

const koffi = require('koffi');
const fs = require('fs');
const path = require('path');
const crypto = require('crypto');

const TITAN_VAULT_SUCCESS = 0;
const PASSWORD = 'smartinventure'; // hardcoded — demo vault only

// Same layout as Demo/_test-cryptomator-vault.
const LAYOUT = [
  { vaultPath: '/Perfect-albums.txt', original: 'Perfect-albums.txt' },
  { vaultPath: '/mysubfolder1/banana.jpg', original: 'banana.jpg' },
  { vaultPath: '/mysubfolder1/mysubfolder2/Rubicon - Rivers - lyrics.txt', original: 'Rubicon - Rivers - lyrics.txt' },
];

function printHelp() {
  console.log(`Usage: node generate-cryptomator-vault.js <vault-directory>

Creates a Cryptomator vault at <vault-directory> with the same layout as the
Demo/_test-cryptomator-vault test vault, using the demo password "${PASSWORD}":

  /Perfect-albums.txt
  /mysubfolder1/banana.jpg
  /mysubfolder1/mysubfolder2/Rubicon - Rivers - lyrics.txt

File contents are copied from Demo/_test-cryptomator-vault/original-files/. Open the
result in the real Cryptomator app (password: ${PASSWORD}) to confirm vaults written by
this library are compatible. Point Cryptomator at <vault-directory> (it must contain the
generated masterkey.cryptomator / vault.cryptomator).

The native library is auto-resolved from ../../Dist/Native/<rid>/ (build it with
../../BuildScripts/build.ps1 -Task aot), or set TITANVAULT_LIB.`);
}

function defaultLibPath() {
  const arch = ({ x64: 'x64', arm64: 'arm64' })[process.arch] || process.arch;
  const rid = (process.platform === 'win32' ? 'win-' : process.platform === 'darwin' ? 'osx-' : 'linux-') + arch;
  const file = process.platform === 'win32' ? 'TitanVault.dll'
             : process.platform === 'darwin' ? 'libTitanVault.dylib' : 'libTitanVault.so';
  return path.resolve(__dirname, '..', '..', 'Dist', 'Native', rid, file);
}

const u8 = (s) => Buffer.byteLength(s, 'utf8');

function load(libPath) {
  const lib = koffi.load(libPath);
  return {
    getLastError: lib.func('titan_vault_get_last_error', 'char *', []),
    createCryptomator: lib.func('titan_vault_create_cryptomator_vault', 'int', ['char *', 'int', 'char *', 'int']),
    loadCryptomator: lib.func('titan_vault_load_cryptomator_vault', 'void *', ['char *', 'int', 'char *', 'int']),
    createDirectory: lib.func('titan_vault_create_directory', 'int', ['void *', 'char *', 'int']),
    writeFile: lib.func('titan_vault_write_file', 'int', ['void *', 'char *', 'int', 'void *', 'int']),
    readFile: lib.func('titan_vault_read_file', 'int', ['void *', 'char *', 'int', 'void *', '_Inout_ int *']),
    closeVault: lib.func('titan_vault_close_vault', 'int', ['void *']),
  };
}

function check(lib, rc, what) {
  if (rc !== TITAN_VAULT_SUCCESS) throw new Error(`${what} failed (rc=${rc}): ${lib.getLastError()}`);
}

function readFileFull(lib, handle, vaultPath) {
  let cap = 1 << 20;
  for (let attempt = 0; attempt < 4; attempt++) {
    const buf = Buffer.alloc(cap);
    const size = [cap];
    const rc = lib.readFile(handle, vaultPath, u8(vaultPath), buf, size);
    if (rc === TITAN_VAULT_SUCCESS) return buf.subarray(0, size[0]);
    if (size[0] > cap) { cap = size[0]; continue; }
    throw new Error(`read_file ${vaultPath} rc=${rc}: ${lib.getLastError()}`);
  }
  throw new Error(`read_file ${vaultPath}: buffer growth failed`);
}

function main() {
  const vaultDir = process.argv[2];
  if (!vaultDir) { printHelp(); process.exit(1); }

  const libPath = process.env.TITANVAULT_LIB || defaultLibPath();
  if (!fs.existsSync(libPath)) {
    console.error(`Native library not found: ${libPath}\nBuild it: ../../BuildScripts/build.ps1 -Task aot  (or build.sh --task aot)`);
    process.exit(1);
  }

  const origDir = path.resolve(__dirname, '..', '_test-cryptomator-vault', 'original-files');
  for (const f of LAYOUT) {
    if (!fs.existsSync(path.join(origDir, f.original))) {
      console.error(`Missing original file: ${path.join(origDir, f.original)}`);
      process.exit(1);
    }
  }

  if (fs.existsSync(path.join(vaultDir, 'masterkey.cryptomator'))) {
    console.error(`A Cryptomator vault already exists at ${vaultDir}. Choose an empty/new directory.`);
    process.exit(1);
  }
  fs.mkdirSync(vaultDir, { recursive: true });

  const lib = load(libPath);

  check(lib, lib.createCryptomator(vaultDir, u8(vaultDir), PASSWORD, u8(PASSWORD)), 'create_cryptomator_vault');
  const handle = lib.loadCryptomator(vaultDir, u8(vaultDir), PASSWORD, u8(PASSWORD));
  if (!handle) throw new Error(`load vault failed: ${lib.getLastError()}`);

  try {
    check(lib, lib.createDirectory(handle, '/mysubfolder1', u8('/mysubfolder1')), 'create /mysubfolder1');
    check(lib, lib.createDirectory(handle, '/mysubfolder1/mysubfolder2', u8('/mysubfolder1/mysubfolder2')), 'create /mysubfolder1/mysubfolder2');

    for (const f of LAYOUT) {
      const data = fs.readFileSync(path.join(origDir, f.original));
      check(lib, lib.writeFile(handle, f.vaultPath, u8(f.vaultPath), data, data.length), `write ${f.vaultPath}`);
      console.log(`  wrote ${f.vaultPath} (${data.length} B)`);
    }

    // Self-check: read the files back through the library and confirm they md5-match the originals.
    let ok = true;
    for (const f of LAYOUT) {
      const got = crypto.createHash('md5').update(readFileFull(lib, handle, f.vaultPath)).digest('hex');
      const want = crypto.createHash('md5').update(fs.readFileSync(path.join(origDir, f.original))).digest('hex');
      if (got !== want) { ok = false; console.log(`  ✗ self-check ${f.vaultPath}: md5 mismatch`); }
    }
    if (!ok) throw new Error('self-check failed (round-trip md5 mismatch)');
  } finally {
    lib.closeVault(handle);
  }

  console.log(`\n✅ Created a Cryptomator vault at: ${vaultDir}`);
  console.log(`   Password: ${PASSWORD}`);
  console.log(`   Layout matches Demo/_test-cryptomator-vault; round-trip md5 verified.`);
  console.log(`   Now open it in the Cryptomator app (with the password above) to confirm compatibility.`);
}

main();
