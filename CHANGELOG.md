# Changelog

Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

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
