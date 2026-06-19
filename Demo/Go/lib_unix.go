//go:build darwin || freebsd || linux

// Unified Vault Format (UVF) for C# and other languages.
// Copyright (c) Smart In Venture 2025- https://www.speedbits.io
// Licensed under AGPL-3.0 (commercial licenses available); see LICENSE.

package main

import "github.com/ebitengine/purego"

// loadLibrary loads the native library on Unix-like platforms via purego.Dlopen (dlopen, no cgo).
func loadLibrary(path string) (uintptr, error) {
	return purego.Dlopen(path, purego.RTLD_NOW|purego.RTLD_GLOBAL)
}
