# UVF / Cryptomator Crypto Library — Audit Plan

This document defines a **repeatable** source-level security audit of the client-side encryption code
in `UvfLib`, covering both **UVF v3** and **Cryptomator v8**. Run it again on any future version and
record the outcome in a timestamped `AUDIT-RESULT.md` (see [Output](#output)).

> **This is a structured self-review to surface obvious flaws. It is _not_ an independent professional
> cryptographic audit, a formal proof, a pentest, or a fuzzing campaign.** It complements — and does not
> replace — the "no professional security audit" caveat in the project [README](../../README.md).

## Goal

Find, classify, and document cryptographic weaknesses (hard-coded key material, weak randomness,
nonce/IV reuse, missing authentication, weak KDFs, non-constant-time comparisons, weak algorithms, poor
key hygiene, info leakage, and custom-crypto correctness issues) — **without changing code**. Fixes are
a separate, per-finding follow-up.

## Scope — files in the audit (full crypto path)

| Area | Files |
|------|-------|
| **Core / Common** | `Scrypt.cs`, `HKDFHelper.cs`, `CipherSupplier.cs`, `AesGcmCryptor.cs`, `AesCtrCryptor.cs`, `AesKeyWrap.cs`, `MacSupplier.cs`, `Masterkey.cs`, `PerpetualMasterkey.cs`, `DestroyableSecretKey.cs`, `CryptographicOperations.cs`, `ReseedingSecureRandom.cs`, `MasterkeyFileAccess.cs`, `EcdhKeyMaterial.cs`, `Pkcs12Helper.cs`, `P384KeyPair.cs`, `CryptomatorVaultConfig.cs`, `SeedIdConverter.cs` |
| **Core / V3 (UVF)** | `AesSivHelper.cs`, `FileContentCryptorImpl.cs`, `FileHeaderCryptorImpl.cs`, `FileHeaderImpl.cs`, `FileNameCryptorImpl.cs`, `DirectoryContentCryptorImpl.cs`, `UVFMasterkeyImpl.cs`, `Constants.cs` |
| **Core / CryptomatorV8** | `FileContentCryptorImpl.cs`, `FileHeaderCryptorImpl.cs`, `FileNameCryptorImpl.cs`, `DirectoryContentCryptorImpl.cs`, `NameShorteningHelper.cs` |
| **Core / Jwe (multi-user)** | `MultiUserJweVaultManager.cs`, `MultiUserJweVaultManager.Scrypt.cs`, `JweStructures.cs` |
| **Vault streams** | `UvfLib.Vault/VaultHelpers/EncryptingStream.cs`, `DecryptingStream.cs` |
| **Master (crypto-relevant)** | `PathTranslators/*` (SIV / dir-id usage), `VaultManager.PublicKey.cs` |

## Methodology

1. **Read every file in scope in full** (not summaries) and trace each crypto operation end to end.
2. **Cross-reference the specs**: RFC 5297 (AES-SIV/S2V), RFC 5869 (HKDF), RFC 7914 (scrypt),
   RFC 3394 (AES-KeyWrap), RFC 5116 / NIST SP 800-38D (AES-GCM), RFC 7518 (JWE, ECDH-ES+A256KW), and the
   Cryptomator v8 / UVF format specs.
3. **Corroborate with tests**: leverage the existing test suite (`build.ps1 -Task test`) and note which
   findings are *verified-by-test* vs *review-only*. Check for official RFC known-answer vectors.
4. **Cite `file:line`** for every observation. Run no destructive tools; change no source.

## Checklist (audit for each of these)

1. **Hard-coded key material** — literal keys/IVs/nonces/salts/passwords in non-test code; separate
   genuine spec constants (format magic, KDF context labels, RFC-3394 `0xA6` IV, `0xFF` reserved field)
   from real secrets.
2. **Randomness** — CSPRNG (`RandomNumberGenerator`) for every secret/nonce/salt; no `System.Random` /
   `new Random()` / time seeds; `Guid.NewGuid()` only for non-secret identifiers.
3. **Nonce / IV uniqueness & reuse** — per-chunk GCM nonces (random 96-bit); document the random-nonce
   birthday bound; header nonces; no counter/derived collisions; per-file content keys.
4. **AEAD / AAD binding & truncation** — chunk index + header nonce bound as AAD (reorder/swap
   resistance); **does decryption detect dropped trailing chunks / a truncated final chunk / a missing
   end-of-stream marker?** Compare UVF-v3 header AAD vs Cryptomator-v8 header (no AAD + `0xFF` reserved).
5. **KDF parameters** — scrypt N/r/p; **PBKDF2 iteration counts & the default method**; HKDF domain
   separation. Compare against OWASP/NIST current guidance.
6. **Key management** — derivation, enc/mac key separation, revolving-seed rotation, and **zeroization**
   (`CryptographicOperations.ZeroMemory` / `Destroy`) of keys, passwords, and derived buffers; note the
   managed-GC key-copy limitation.
7. **Constant-time comparison** — tags/MACs/passwords via `FixedTimeEquals`, never `==`/`SequenceEqual`.
8. **Algorithm choices** — AES-GCM/SIV/CTR/CBC; **SHA-1** usage (where, on what input); no ECB-for-data;
   key sizes; EC curve; legacy algorithms.
9. **Custom-crypto correctness** — the hand-rolled `Scrypt`, `HKDFHelper`, AES-CTR, AES-SIV (S2V); any
   "placeholder"/incomplete implementations; **RFC known-answer-vector coverage**.
10. **Multi-user / public-key** — JWE recipients, ECDH-ES+A256KW Concat-KDF correctness, CEK wrapping,
    the PBKDF2→AES-GCM private-key wrap, and **revocation semantics** (remove ⇒ future access only).
11. **Error handling / info leakage** — generic auth-failure messages, no secrets in exceptions/logs,
    debug-print gating, no padding/timing oracle distinguishers.
12. **Cross-platform / AOT crypto consistency** — platform-dependent crypto behavior (e.g. encrypted
    PKCS#8 / PBES2 differences between CNG and OpenSSL/AOT) that round-trips on one OS but not another.

## Output

Write findings to **`AUDIT-RESULT.md`** in this folder. The **first line is a run timestamp** + the
audited git commit/tag. Each finding: `ID · Area · File:line · Severity (Critical/High/Medium/Low/Info)
· Confidence · Description · Impact · Recommendation`, followed by an overall assessment, a
count-by-severity summary, and a prioritized remediation list. A re-run overwrites `AUDIT-RESULT.md`
(git history keeps prior runs); keep dated copies `AUDIT-RESULT-<date>.md` if a visible history is wanted.
