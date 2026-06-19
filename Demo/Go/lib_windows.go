//go:build windows

package main

import "syscall"

// loadLibrary loads the native library on Windows.
//
// purego's Dlopen/RTLD_* are unix-only (their build tag is darwin || freebsd || linux), so on Windows
// we load the DLL with the stdlib syscall.LoadLibrary and hand its HMODULE to purego.RegisterLibFunc —
// which IS available on Windows. This keeps the demo cgo-free with no extra dependencies.
func loadLibrary(path string) (uintptr, error) {
	h, err := syscall.LoadLibrary(path)
	if err != nil {
		return 0, err
	}
	return uintptr(h), nil
}
