# .NET demo (managed `UvfLib` package)

Pure managed C# — no native DLL. Uses the `UvfLib.Master` NuGet package and its `VaultManager` +
`EncryptingStorage` (`IStorage`) API to create a vault, encrypt/decrypt files, list a directory, and
delete a file, for both UVF and Cryptomator.

## Run

```bash
dotnet run -- both          # uvf | cryptomator | both
```

Until `UvfLib` is published to nuget.org, add the local package feed as a restore source:

```bash
dotnet restore --source https://api.nuget.org/v3/index.json --source D:\NugetFeed
dotnet run --no-restore -- both
```

(Produce the local package with: `dotnet pack Uvf.Net/UvfLib.Master/UvfLib.Master.csproj -c Release
-p:PublishAot=false -p:NativeLib= -p:SelfContained=false -p:RuntimeIdentifier= -o D:\NugetFeed`.)

## Key snippet

```csharp
using var vault = await VaultManager.CreateUvfVaultAsync(vaultDir, password.ToCharArray());
IStorage fs = vault.EncryptingStorage;          // transparently encrypts content + names
await fs.CreateDirectoryAsync("/notes");
// write/read via fs.OpenAsync/WriteAsync/ReadAsync (see Program.cs helpers)
var entries = await fs.ReadDirAsync("/");        // decrypted names
await fs.DeleteAsync("/hello.txt");
```
