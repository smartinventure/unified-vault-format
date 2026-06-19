# Actions Taken — 2026-06-19 Audit

Remediation log for the findings in [`AUDIT-RESULT.md`](AUDIT-RESULT.md). The audit itself was
report-only; the actions below were applied afterward as reviewed, test-verified follow-ups.

| Finding | Severity | Action | Status |
|---------|:--------:|--------|--------|
| **F-01** — UVF default KDF too weak (PBKDF2 @ 64k) | Medium | Default raised to **PBKDF2-HMAC-SHA512 @ 210,000** (OWASP 2023) across all four sites — Core + Master `KeyDerivationParameters`, the JWE `DefaultPbkdf2Iterations` fallback, and the native-export `ConvertKdfParameters`; `Validate()` floor raised 1,000 → 100,000. Standard interoperable alg `PBES2-HS512+A256KW` kept (scrypt remains opt-in). Existing vaults still open at their stored `p2c`. | ✅ Fixed |
| **F-02** — whole-chunk truncation undetected | Medium | **Documented** as a known limitation in the project [README](../../README.md) "Security model & known limitations" section (matches the Cryptomator format's design; per-chunk content remains authenticated). No format change made. | ✅ Documented |
| **F-03** — dead "simplified RFC-3394" `AesWrapTransform` | Low | **Removed** the placeholder class, the `RFC3394_KEYWRAP` supplier, and the `"AES-WRAP"` branch from `CipherSupplier.cs` (all unreferenced; real key-wrap is the BouncyCastle `AesKeyWrap`). | ✅ Fixed |
| **F-04** — missing RFC/NIST known-answer vectors | Low | **Added** KAT tests to `UvfLib.Tests/common/`: AES Key Wrap (RFC 3394 §4.6), AES-256-SIV (published cross-impl vector — RFC 5297's own vectors are AES-128-only and can't drive this AES-256 impl), and AES-256-GCM (NIST CAVP + Go-crypto vectors). scrypt (RFC 7914) and HKDF (RFC 5869) vectors already existed. | ✅ Fixed |
| **F-05** — SHA-1 for name-shortening / dir-id hashing | Info | No action — Cryptomator-spec-mandated, applied to ciphertext (collision-irrelevant); changing it would break Cryptomator compatibility. | ⏸ Accepted |
| **F-06** — GCM 96-bit random nonce bound | Info | No action — safe to ~128 TiB per file under a per-file content key; documented in the result. | ⏸ Accepted |
| **F-07** — Cryptomator v8 header has no AAD | Info | No action — matches the Cryptomator spec (integrity via the encrypted `0xFF…` reserved field). | ⏸ Accepted |
| **F-08** — managed runtime can't guarantee key erasure | Low | **Documented** in the README "Security model & known limitations" section as a residual risk. | ✅ Documented |
| **F-09** — cross-platform PBES2 private-key wrap | Info | Already fixed in v1.0.3 (portable PBKDF2→AES-GCM wrap); a `ubuntu-latest` demo-run CI job guards against regressions. | ✅ Fixed (v1.0.3) |
| **F-10** — AI-assisted port, no independent audit | Info | No action — disclosed prominently in the README disclaimer; this self-review is not a substitute. | ⏸ Accepted |

## Verification

- `UvfLib.Tests` full suite green after F-01, F-03, and F-04 changes (no regressions; old vaults still open).
- F-01/F-03/F-04 shipped in release **v1.0.4** (all six native binaries rebuilt + managed packages on nuget.org).
- No production code changed for F-02/F-05/F-06/F-07/F-08/F-10 (documentation / accepted).

## Not done (deliberately)

- **F-02 format change** (authenticated total length / final-chunk marker for UVF) — would diverge from the
  current UVF/Cryptomator chunk format; left as a documented limitation pending a format-level decision.
- **F-05 SHA-1 → SHA-256** — would break Cryptomator interoperability; not changed.
