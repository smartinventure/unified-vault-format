# Audit

Source-level security reviews of the UVF / Cryptomator cryptographic code in this repository. Each
audit is a **repeatable** review (a plan) plus a **timestamped snapshot** of what it found (a result)
and a log of what was changed in response (actions taken).

> ⚠️ These are **structured self-reviews**, not independent professional cryptographic audits. They
> complement — and do not replace — the "no professional security audit" caveat in the project
> [README](../README.md).

## Audit runs

### [`Audit-2026-06-19/`](Audit-2026-06-19/) — 2026-06-19 (commit `d0735c0`, tag `v1.0.3`)

- **[AUDIT-PLAN.md](Audit-2026-06-19/AUDIT-PLAN.md)** — the audit that was performed: scope (the full
  client-side crypto path), methodology (manual review cross-referenced to the RFCs + the test suite),
  and the 12-point checklist. **Re-run this** against future versions.
- **[AUDIT-RESULT.md](Audit-2026-06-19/AUDIT-RESULT.md)** — the findings from this run (timestamp +
  audited commit at the top): **0 Critical · 0 High · 2 Medium · 2 Low · 6 Info**.
- **[ACTIONS-TAKEN.md](Audit-2026-06-19/ACTIONS-TAKEN.md)** — what was fixed/documented/accepted in
  response (F-01 KDF default raised, F-03 dead code removed, F-04 KAT tests added, F-02/F-08 documented,
  the rest accepted with rationale).

## How to run an audit

1. Follow the checklist + methodology in the latest `AUDIT-PLAN.md`.
2. Write the findings to a new dated folder `Audit-YYYY-MM-DD/AUDIT-RESULT.md`, with a run timestamp and
   the audited git commit/tag on the first line.
3. Record any remediation in that folder's `ACTIONS-TAKEN.md`.
4. Add a row to **Audit runs** above.
