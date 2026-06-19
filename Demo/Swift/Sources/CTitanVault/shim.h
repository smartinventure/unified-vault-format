// Bridges the canonical TitanVault C ABI header into the `CTitanVault` Swift
// module. The actual header lives at <repo>/Bindings/include/titan_vault.h and
// is reached via the headerSearchPath set on this target in Package.swift, so
// the typedef'd function-pointer types (decltype'd in the C++ demo) become
// available to Swift for `unsafeBitCast` after dlsym.
#include "titan_vault.h"
