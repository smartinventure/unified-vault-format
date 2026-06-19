**Audit run:** 2026-06-19 08:14 UTC · **Commit:** `d0735c0` (tag `v1.0.3`) · **Auditor:** automated source review (Claude) · **Method:** see [`AUDIT-PLAN.md`](AUDIT-PLAN.md)

# UVF / Cryptomator Crypto Library — Audit Result

> ⚠️ **This is a structured self-review, not an independent professional cryptographic audit.** It reads
> the source against a checklist and the relevant RFCs to surface obvious flaws. It is **not** a formal
> proof, pentest, or fuzzing campaign, and does not replace the professional audit recommended in the
> [README](../../README.md) disclaimer. Findings below are best-effort and may be incomplete.

## Scope audited

Full client-side crypto path (per [`AUDIT-PLAN.md`](AUDIT-PLAN.md)): `UvfLib.Core/Common` (primitives,
KDFs, key management, RNG), `UvfLib.Core/V3` (UVF v3), `UvfLib.Core/CryptomatorV8`, `UvfLib.Core/Jwe`
(multi-user), `UvfLib.Vault/VaultHelpers` (streaming), and the crypto-relevant parts of
`UvfLib.Master` (SIV/path usage, public-key membership).

## Summary

| Severity | Count |
|----------|:-----:|
| Critical | 0 |
| High | 0 |
| Medium | 2 |
| Low | 2 |
| Info / Resolved | 6 |

**Overall:** The cryptographic design is sound and follows the Cryptomator/UVF reference closely. AEAD
(AES-GCM) protects content with per-chunk random nonces and chunk-index + header-nonce AAD binding;
filenames use AES-SIV; keys are CSPRNG-generated, domain-separated via HKDF, compared in constant time,
and zeroized after use. The two **Medium** items are a below-current-guidance PBKDF2 default for UVF
vaults and the (Cryptomator-inherited) lack of whole-file truncation detection. No hard-coded secrets,
weak RNG, or broken primitives were found. Remediation is deferred to a separate pass (this run is
report-only).

## Findings

### F-01 · Weak default password KDF for UVF vaults · **Medium** · Confidence: High
- **Area:** KDF parameters · **File:** `Uvf.Net/UvfLib.Core/Api/KeyDerivationParameters.cs:14,21,105`; used in `Uvf.Net/UvfLib.Core/Jwe/MultiUserJweVaultManager.cs:23,~570,580-585`
- **Description:** New UVF vaults default to **PBKDF2-HMAC-SHA512 with 64,000 iterations** (`Method = PBKDF2_HMAC_SHA512`, `Pbkdf2Iterations = 64000`). OWASP's 2023 Password Storage guidance for PBKDF2-HMAC-SHA512 is **≥ 210,000** iterations (~3.3× higher). The `Validate()` floor is only **1,000** iterations. Scrypt (memory-hard, N=2^15) is available but **opt-in** (`KeyDerivationParameters.Scrypt()`); the Cryptomator format path already uses scrypt N=2^15 (`MasterkeyFileAccess.cs:16-18,339`) and is unaffected.
- **Impact:** A weaker default lowers the cost of offline brute-force/dictionary attacks against a stolen UVF vault if the user's passphrase is weak. Not a break; it weakens the safety margin for the default configuration.
- **Recommendation:** Raise the PBKDF2-HMAC-SHA512 default to ≥ 210,000, **or** make scrypt the default for new UVF vaults; raise the `Validate()` minimum (e.g. ≥ 100,000 for PBKDF2). Keep reading older vaults at their stored parameters for compatibility.
- **✅ Remediated 2026-06-19:** default raised to **210,000** (OWASP-2023 for PBKDF2-HMAC-SHA512) across `KeyDerivationParameters` (Core + Master), the JWE fallback, and the native export; `Validate()` floor raised to **100,000**. The standard interoperable alg `PBES2-HS512+A256KW` is kept (scrypt remains opt-in). Existing vaults still open at their stored `p2c`. Full suite green (281 pass).

### F-02 · Whole-chunk truncation is not detected on decryption · **Medium** · Confidence: High
- **Area:** AEAD / AAD binding · **File:** `Uvf.Net/UvfLib.Vault/VaultHelpers/DecryptingStream.cs:236-251`; AAD at `:256` and `Uvf.Net/UvfLib.Core/V3/FileContentCryptorImpl.cs:233-236`
- **Description:** Each chunk is AES-GCM with AAD = `bigEndian(chunkNumber) ‖ headerNonce`, which prevents **reordering/swapping/replay** of chunks. However, the decrypt loop stops at clean EOF (`bytesRead == 0` → `return false`, no error); only a *partial* trailing chunk (< nonce+tag = 28 bytes) raises `InvalidCiphertextException`. There is **no authenticated total length or end-of-stream marker**, so deleting whole trailing chunks yields a **silently truncated plaintext**.
- **Impact:** An attacker who can modify the ciphertext at rest can drop trailing data undetectably (the retained content stays authentic). This is a **known, documented property of the Cryptomator format** as well — so it is "as-designed" for Cryptomator compatibility — but it is worth stating explicitly for UVF.
- **Recommendation:** Document the property in the security model. For UVF (where compatibility allows), consider binding the total chunk count / a final-chunk flag into the last chunk's AAD, or an authenticated length in the header, to make truncation detectable.

### F-03 · Dead "simplified RFC-3394" AES-KeyWrap placeholder · **Low** · Confidence: High
- **Area:** Custom-crypto / dead code · **File:** `Uvf.Net/UvfLib.Core/Common/CipherSupplier.cs:147,366-478`
- **Description:** `AesWrapTransform` is a self-described "simplified RFC 3394 placeholder, not full implementation," reachable only via `CreateTransform("AES-WRAP")` at `:147`, which **nothing calls**. All real key wrapping uses the BouncyCastle-backed `AesKeyWrap.WrapKey` (`Common/AesKeyWrap.cs`), invoked by the JWE CEK wrapping (`MultiUserJweVaultManager.cs:590,708`, `.Scrypt.cs:131`).
- **Impact:** None today (dead code), but a partial/incorrect key-wrap implementation left in the tree is a latent foot-gun if a future change routes through it.
- **Recommendation:** Delete `AesWrapTransform` (and the `"AES-WRAP"` branch), or replace its body with a delegation to `AesKeyWrap` and a clear comment.

### F-04 · Missing RFC known-answer vectors for SIV / KeyWrap / GCM · **Low** · Confidence: High
- **Area:** Custom-crypto correctness (test coverage) · **Files:** `Uvf.Net/UvfLib.Tests/common/AesKeyWrapTest.cs`, `Uvf.Net/UvfLib.Tests/v3/FileNameCryptorImplTest.cs`
- **Description:** Scrypt (RFC 7914) and HKDF (RFC 5869) are validated against official vectors (`ScryptTest.cs`, `HKDFHelperTest.cs`). **AES-SIV/S2V (RFC 5297), AES-KeyWrap (RFC 3394), and AES-GCM** are covered only by round-trip / interop tests, not by published known-answer vectors. SIV correctness is *indirectly* corroborated by the proven real-Cryptomator round-trip interop and the Java-reference test data.
- **Impact:** Lower assurance that the hand-rolled S2V (and the wrap path) exactly match the standards in every edge case (e.g. empty/▸multiple AD components, block-boundary lengths) beyond what interop exercises.
- **Recommendation:** Add RFC 5297 and RFC 3394 known-answer vectors (and a couple of NIST GCM KATs) to the test suite.

### F-05 · SHA-1 used for name-shortening and directory-ID hashing · **Info** · Confidence: High
- **Area:** Algorithm choice · **Files:** `Uvf.Net/UvfLib.Core/CryptomatorV8/NameShorteningHelper.cs:37`, `Uvf.Net/UvfLib.Core/CryptomatorV8/FileNameCryptorImpl.cs:67`, `Uvf.Net/UvfLib.Master/PathTranslators/UvfPathTranslator.cs:136`
- **Description:** SHA-1 hashes the **already-AES-SIV-encrypted** filename/dir-id to produce `.c9s` shortened names and directory hashes. This is **mandated by the Cryptomator v8 spec**. The input is ciphertext, so SHA-1 collision resistance is not security-relevant here (a collision would at most cause a storage-path clash, not a confidentiality/integrity break).
- **Impact:** None for confidentiality/integrity. Changing it would break Cryptomator compatibility.
- **Recommendation:** No action. Keep for compatibility; note the rationale (done here).

### F-06 · GCM random 96-bit nonce reuse bound · **Info** · Confidence: High
- **Area:** Nonce/IV reuse · **Files:** `Uvf.Net/UvfLib.Core/V3/FileContentCryptorImpl.cs:225-226`, `Uvf.Net/UvfLib.Core/CryptomatorV8/FileContentCryptorImpl.cs:91`, `Constants.cs:28`
- **Description:** Each 32 KiB chunk gets a fresh **random 96-bit** GCM nonce under a **per-file content key**. By NIST SP 800-38D, random 96-bit IVs keep collision probability ≤ 2^-32 for up to ~2^32 invocations under one key → ~**128 TiB per file** at 32 KiB/chunk. The per-file key resets the budget for every file.
- **Impact:** Negligible for any realistic file size; a single file would need to exceed ~128 TiB to approach the bound.
- **Recommendation:** No action. Optionally document the per-file size ceiling.

### F-07 · Cryptomator v8 header carries no GCM AAD · **Info** · Confidence: High
- **Area:** AEAD design · **File:** `Uvf.Net/UvfLib.Core/CryptomatorV8/FileHeaderCryptorImpl.cs:74-75,111-112`
- **Description:** The Cryptomator v8 file header GCM uses an empty AAD; integrity is provided by encrypting the reserved `0xFFFFFFFFFFFFFFFF` field (checked on decrypt). UVF v3 instead binds `magic ‖ seedId` as header AAD (`V3/FileHeaderCryptorImpl.cs:103-106`). Both match their respective specs.
- **Impact:** None — as-designed and required for Cryptomator interoperability.
- **Recommendation:** No action.

### F-08 · Managed runtime cannot guarantee key erasure · **Low** · Confidence: Medium
- **Area:** Key hygiene · **Files:** `Uvf.Net/UvfLib.Core/Common/CryptographicOperations.cs`, `DestroyableSecretKey.cs`, `Masterkey.cs`
- **Description:** The code diligently zeroizes keys/passwords/derived buffers with `CryptographicOperations.ZeroMemory` and `Destroy()`. However, on .NET the GC may **relocate or copy** `byte[]`/`char[]` before zeroization, and swap/hibernation can persist secrets — an inherent managed-runtime limitation, not a code defect.
- **Impact:** Residual secrets may linger in process memory or swap despite best-effort wiping.
- **Recommendation:** Document the residual risk. (Optional, advanced: pinned/`fixed` buffers or `System.Security.SecureString`-style handling for the highest-value secrets — large effort, limited benefit.)

### F-09 · Cross-platform private-key wrap (PBES2) · **Info — Resolved in v1.0.3** · Confidence: High
- **Area:** Cross-platform / AOT crypto · **File:** `Uvf.Net/UvfLib.Core/Common/EcdhKeyMaterial.cs`
- **Description:** Public-key membership originally stored the user private key via the platform's
  encrypted-PKCS#8 (PBES2), which did not round-trip between Windows (CNG) and Linux/macOS (OpenSSL/AOT).
  Fixed in v1.0.3 by wrapping plain PKCS#8 with portable **PBKDF2-HMAC-SHA256 → AES-256-GCM**; verified on
  Linux (Python + Node demos) and Windows (full test suite).
- **Impact:** Resolved. Retained here as a reminder to prefer portable primitives over OS-provided PBE.
- **Recommendation:** Keep using portable primitives; the Linux demo CI job now guards this.

### F-10 · Provenance: AI-assisted port, no independent audit · **Info** · Confidence: High
- **Area:** Process · **File:** project-wide (see [README](../../README.md) disclaimer)
- **Description:** The C# code was ported from the Java reference with extensive AI assistance and has had
  no independent professional security audit. This self-review is not a substitute.
- **Recommendation:** Obtain an independent professional audit before using to protect high-value data.

## Positive observations (no finding)

- **CSPRNG everywhere** — all keys/nonces/salts use `RandomNumberGenerator`; no `System.Random`/`new Random()` in crypto code. `Guid.NewGuid()` appears only for non-secret identifiers (dir UUIDs, JTI).
- **Constant-time comparison** — SIV tag and MAC verification use `CryptographicOperations.FixedTimeEquals` (`AesSivHelper.cs:333`, `MacSupplier.cs:143,167`, `MasterkeyFileAccess.cs`).
- **AAD binding** — content chunks bind `chunkNumber ‖ headerNonce`, giving reorder/swap/replay resistance.
- **Key separation & rotation** — distinct enc/mac subkeys; HKDF with per-purpose context labels (`"fileHeader"`, `"siv"`, `"hmac"`, `"dirHash"`, `"rootDirId"`); revolving seeds for UVF rotation.
- **Validated KDFs** — scrypt (RFC 7914) and HKDF (RFC 5869) pass official test vectors.
- **Spec constants only** — the only hard-coded byte arrays are format magic, KDF context labels, the RFC-3394 `0xA6` integrity IV, and the `0xFF` reserved field — no hard-coded secrets.
- **Interop proven** — reads vaults created by the Cryptomator app, and vaults it writes open in the Cryptomator app (round-trip tested), which transitively validates the SIV/header/content crypto against the reference.

## Prioritized remediation (separate follow-up — not done in this pass)

1. ~~**F-01 (Medium)** — raise the UVF PBKDF2 default to ≥ 210,000 (or default to scrypt) + raise the `Validate()` floor.~~ ✅ **Done 2026-06-19** (default 210,000, floor 100,000; standard PBES2 alg kept).
2. **F-02 (Medium)** — document truncation behavior; optionally add an authenticated final-chunk/length marker for UVF.
3. **F-03 (Low)** — remove/redirect the dead `AesWrapTransform` placeholder.
4. **F-04 (Low)** — add RFC 5297 / RFC 3394 / NIST-GCM known-answer vectors.
5. **F-08 (Low)** — document the managed-memory key-erasure limitation.
6. **F-05/06/07/09/10 (Info)** — no code change; documentation/awareness only.
