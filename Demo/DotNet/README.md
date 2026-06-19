# .NET demo (managed `UvfLib` package)

Pure managed C# — **no native DLL**. Uses the `UvfLib.Master` NuGet package and its `VaultManager` API
(plus real `System.IO.Stream`s and `EncryptingStorage`). This is the managed-API counterpart of the
native reference [`../NodeJs/vault-demo.js`](../NodeJs/vault-demo.js): it covers the **same capabilities**
section-for-section — Detect format, File, Text helpers, Directory, Streaming, Persistence, key
rotation, password + public-key multi-user (incl. `ChangeUvfUserPasswordAsync` / `RemoveUserFromVaultAsync`),
and Maintenance (`BackupVaultFilesAsync`, secure-zero, password change) — then the real-Cryptomator
interop and a benchmark.

## Restore the package (until it's on nuget.org)

The packages aren't published yet, so produce a **local feed** once:

```powershell
../../BuildScripts/build.ps1 -Task pack        # -> Dist/Packages/nuget/UvfLib.*.1.0.4.nupkg
```

The committed [`nuget.config`](nuget.config) already points at that folder (and clears any inherited
internal feeds), so no extra flags are needed. Once `UvfLib` is on nuget.org, delete `nuget.config`.

## Run

```bash
dotnet run -c Release                          # FULL run: both formats + real-Cryptomator interop + a quick benchmark
dotnet run -c Release -- --format uvf          # one format's functional sections
dotnet run -c Release -- --benchmark --size 2  # throughput only, 2 GB
dotnet run -c Release -- --cryptomator-interop
```

**Switches** (`--format`, `--benchmark`, `--size <GB>`, `--cryptomator-interop`, `--vault`, `--password`)
mirror the other demos — see the shared table in
[`../README.md`](../README.md#command-line-options-all-native-demos). There is no `--lib` here (managed
package, no native library).

## Key snippet

```csharp
using var vault = await VaultManager.CreateUvfVaultAsync(vaultDir, password.ToCharArray());
await vault.WriteAllTextAsync("/notes/todo.txt", "buy milk");      // transparent encryption
string text = await vault.ReadAllTextAsync("/notes/todo.txt");
await using (Stream s = await vault.OpenWriteAsync("/big.bin")) { /* stream large data */ }
await VaultManager.AddUserToVaultAsync(vaultDir, adminPw, "alice", alicePw);   // multi-user
var (pub, encPriv) = VaultManager.GenerateUserKeyPair(keyPw);                  // public-key membership
await VaultManager.RotateVaultKeysAsync(vaultDir, adminPw);                    // key rotation
```
