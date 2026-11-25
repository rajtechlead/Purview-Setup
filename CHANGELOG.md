# Changelog

Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Added

- **Tamper-evident audit log**: every entry stores the SHA-256 of its canonical fields plus the previous entry's hash. `IAuditLog.VerifyAsync` recomputes the chain in insertion order and names the first broken entry; existing databases are backfilled on first open. A **Verify audit log integrity** action is available in Settings.
- **Provider health circuit breaker**: the AI failover orchestrator now probes each candidate with a five-second timeout before dispatch, records `Unhealthy` attempts, and cools a failing provider down for two minutes so subsequent calls go straight to the fallback.
- **DPAPI secret protection**: the lab Entra client secret is encrypted at rest with Windows DPAPI (`CurrentUser`) through `DpapiSecretProtector`, which uses crypt32 directly and adds no package. Legacy plaintext secrets are encrypted on the next save; a secret protected under another profile decrypts to empty rather than throwing.

### Added (Phase 1.2)

- **Real PowerShell payloads**: `PurviewScriptLibrary` generates idempotent cmdlets for labels, label policies, auto-labelling, DLP create/enforce, retention labels, retention policies, audit enablement and audit retention. Controls without a reliable cmdlet are emitted as explicit `MANUAL` steps with the verification command.
- **Rollback script**: generated in reverse operation order, naming the pre-change backup and leaving destructive removals commented for approval.
- **Configuration-as-code**: change plans export as a portable JSON package and as `.ps1`, and import back with a new plan id so simulation and approval cannot be bypassed. Deployment gained Export JSON / Export PowerShell / Export rollback / Import plan / Apply (gated) actions.
- **Command state refresh**: plan-dependent buttons now enable and disable correctly after a plan is built or imported.

### Added (Phase 1.3)

- **PDF, DOCX and XLSX exports**: a paginated PDF 1.4 writer with base-14 fonts, plus OOXML DOCX and XLSX packages built directly with `ZipArchive` — no new dependencies. XLSX carries Scores, Findings and Frameworks sheets. All three are available from Reports.
- **Binary report path**: `IReportExporter.RenderBytes` with a text default, and `GovernanceWorkspace.ExportReportAsync` now writes bytes so every format shares one audited export path.
- **Persona dashboards, risk heatmap and trends**: Executive, Security, Governance and Compliance tabs; module × severity heatmap of open findings; the last twelve overall scores charted from stored assessments with maturity trail and delta.
- **MSP tenancy**: a `tenant_profiles` registry with DPAPI-protected credentials, save/activate from Settings, in-session evidence cleared on activation, and a cross-tenant Portfolio tab built from each tenant's latest assessment.
- **Continuous monitoring**: an optional in-app check interval (minimum 15 minutes) that re-runs the read-only assessment and refreshes drift and licence signals, plus locally generated alerts for drift, score regressions and critical failures.

### Changed

- `AppSettings` is now a record so the settings document can be copied with a protected secret without mutating the live object.
- Test suite expanded from 36 to 51 tests (audit chain, tampering, DPAPI round trip, unhealthy provider, cooldown, real payloads, manual fallback, rollback order, config-package round trip).
- Roadmap Phase 1.1 is 3 of 5 complete; Phase 1.2 is 2 of 4 complete with rollback and config-as-code partially done. SQLCipher page encryption, a signed build/update channel, a live executor, per-object config export and ARM/Bicep/Terraform remain open.

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
