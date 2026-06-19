// swift-tools-version:5.7
//
// SwiftPM package for the TitanVault Swift demo. Builds two targets:
//   - CTitanVault: a thin C target that exposes the canonical C ABI header
//     (Bindings/include/titan_vault.h) to Swift as the module `CTitanVault`.
//     We do NOT link the native library at build time — the demo loads it at
//     runtime with dlopen/dlsym (see Sources/vault-demo/main.swift), so the
//     same auto-discovery + `--lib` flag work as in the other demos and the
//     package builds even when the .dylib/.so has not been produced yet.
//   - vault-demo: the executable, a full-parity port of ../NodeJs/vault-demo.js.
//
// The header search path is relative to the CTitanVault *target* directory
// (Sources/CTitanVault). From there the canonical header lives four levels up:
//   Sources/CTitanVault -> Sources -> Swift -> Demo -> <repo root> -> Bindings/include
import PackageDescription

let package = Package(
    name: "vault-demo",
    targets: [
        // C shim target: the modulemap + shim.h pull in titan_vault.h so the
        // typedef'd function-pointer types are visible to Swift. No sources to
        // compile other than the header include, so this produces no objects to link.
        .target(
            name: "CTitanVault",
            // The custom module.modulemap + shim.h sit directly in the target
            // directory (not the default `include/` subfolder), so point the
            // public headers path at the target root.
            publicHeadersPath: ".",
            cSettings: [
                .headerSearchPath("../../../../Bindings/include")
            ]
        ),
        .executableTarget(
            name: "vault-demo",
            dependencies: ["CTitanVault"]
        ),
    ]
)
