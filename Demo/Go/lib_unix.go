//go:build darwin || freebsd || linux

package main

import "github.com/ebitengine/purego"

// loadLibrary loads the native library on Unix-like platforms via purego.Dlopen (dlopen, no cgo).
func loadLibrary(path string) (uintptr, error) {
	return purego.Dlopen(path, purego.RTLD_NOW|purego.RTLD_GLOBAL)
}
