# Changelog

Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Added

- **Tamper-evident audit log**: every entry stores the SHA-256 of its canonical fields plus the previous entry's hash. `IAuditLog.VerifyAsync` recomputes the chain in insertion order and names the first broken entry; existing databases are backfilled on first open. A **Verify audit log integrity** action is available in Settings.
- **Provider health circuit breaker**: the AI failover orchestrator now probes each candidate with a five-second timeout before dispatch, records `Unhealthy` attempts, and cools a failing provider down for two minutes so subsequent calls go straight to the fallback.
- **DPAPI secret protection**: the lab Entra client secret is encrypted at rest with Windows DPAPI (`CurrentUser`) through `DpapiSecretProtector`, which uses crypt32 directly and adds no package. Legacy plaintext secrets are encrypted on the next save; a secret protected under another profile decrypts to empty rather than throwing.

### Changed

- `AppSettings` is now a record so the settings document can be copied with a protected secret without mutating the live object.
- Test suite expanded from 36 to 41 tests (audit chain, tampering, DPAPI round trip, unhealthy provider, cooldown).
- Roadmap Phase 1.1 is 3 of 5 complete; SQLCipher page encryption and a signed build/update channel remain open.

## [1.0.0] - 2026-11-25

### Added

- **Domain**: 16-module, 48-control catalog; deterministic scoring (security, governance, compliance, maturity, risk, AI governance, Copilot readiness); sensitive-data detection with Luhn validation; redaction/tokenization/masking/blocking.
- **Application**: assessment orchestration, recommendation generation, change-plan builder with change-management pack, 3-stage approval state machine, backup capture, drift detection, licence optimisation, framework mapping, HTML/JSON/CSV/Markdown report exporters, AI failover orchestrator, privacy-guarded copilot service.
- **Infrastructure**: Microsoft Graph discovery (licensing, users, roles, conditional access, secure score, devices, app secrets); read-only Security & Compliance PowerShell collection; SQLite persistence with schema; Windows certificate lifecycle (create/import/export/install/rotate/remove/expiry); Key Vault PFX storage over REST; Entra certificate/managed identity/interactive/secret credentials; OpenAI-compatible, Anthropic and Gemini adapters; change simulation and rollback validation.
- **UI**: WPF shell with executive dashboard, assessment, deployment/approval, monitoring, reports, AI assistant, settings/certificate wizard, searchable help center and about page.
- **Tooling**: 36 automated tests, WiX MSI project, MSIX manifest, GitHub Actions CI, full documentation set.

### Known limitations

- Live tenant mutation is disabled in this build; apply generates the reviewed script instead of executing it.
- PDF/DOCX/XLSX exports are not implemented; HTML is print-ready for PDF.
- ARM/Bicep/Terraform export and multi-tenant MSP switching are not implemented.
- MSI/MSIX are authored but not built in this environment (WiX toolset not installed).
