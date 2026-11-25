# 06 — Future Roadmap

Ordered by risk reduction and by what the current build deliberately leaves out.

## Phase 1.1 — Close the safety gaps

**3 of 5 complete.** Updated 25 November 2026.

| Item | Status | Why | Notes |
| --- | --- | --- | --- |
| Encrypted local store | **Open** | Assessment evidence currently sits in a plaintext SQLite file | Page-level encryption still needs SQLCipher (or a SQLite encryption build). The lab client secret is now DPAPI-protected, which reduces but does not remove the exposure of assessment, plan and backup payloads. |
| Signed builds and update channel | **Open** | Enterprise deployment requires Authenticode | CI signing step is authored but commented out; needs a signing certificate and a published SHA-256 manifest. No auto-update channel exists. |
| Hash-chained audit log | **Done** | Local DB is user-writable | Each row stores the SHA-256 of its canonical fields plus the previous hash, in insertion order. Legacy rows are backfilled on first open; `IAuditLog.VerifyAsync` recomputes the chain; Settings has a **Verify audit log integrity** button. Tamper detection is covered by tests. |
| Provider health pre-check | **Done** | `IsHealthyAsync` existed but the failover loop did not consult it | Every candidate is probed with a 5-second timeout. Unhealthy providers are skipped and recorded as `Unhealthy`, and any failure puts that provider in a 2-minute cooldown so the next call goes straight to the fallback. |
| Per-user secret store for lab mode | **Done** | Client secret landed in the settings document | `DpapiSecretProtector` uses crypt32 DPAPI with `CurrentUser` scope, so no key material sits beside the data and another profile cannot decrypt it. Values are transparently re-encrypted on save and decrypted on load; a profile change yields an empty secret rather than a crash. |

## Phase 1.2 — Deployment depth

- **Live executor profile**: separately installed, signed component with an explicit "execute" approval gate; writes a signed change record and verifies post-conditions.
- **Real operation payloads**: per-module PowerShell templates for label, DLP, retention and audit policies, validated against the module's own `Get-*` output before apply.
- **Rollback execution**: apply the backup package through the same executor, with post-rollback assessment comparison.
- **Configuration-as-code**: export/import of labels, DLP, retention and audit retention as JSON and PowerShell; ARM/Bicep for the Azure-side Data Map; Terraform provider notes where one exists.

## Phase 1.3 — Reporting and multi-tenant

- **PDF, DOCX and XLSX** exporters (QuestPDF or a permissive PDF library; DocumentFormat.OpenXml for DOCX/XLSX).
- **Risk heatmap** view (likelihood × impact) and trend lines across assessment history.
- **Multi-tenant MSP mode**: tenant switcher, per-tenant isolation in the database, cross-tenant dashboard and per-customer role mapping.
- **Scheduled monitoring**: background timer or Windows Task Scheduler integration for weekly assessment and drift alerts.

## Phase 2 — AI and compliance depth

- **Microsoft Security Copilot** integration where a tenant-licensed endpoint is available.
- **Deeper AI governance**: prompt/response classification history, model inventory, ISO 42001 control mapping, agent governance.
- **Compliance Manager API**: real improvement-action ingestion and score instead of a placeholder posture value.
- **Defender integration**: exposure management signals and device risk feeding endpoint DLP readiness.
- **Government clouds**: configurable Graph/EXO endpoints for GCC/GCC High/DoD.
- **Custom sensitive information types**: author and validate custom SITs as part of deployment.

## Phase 3 — Platform

- **WinUI 3 shell** consuming the same Application layer, with Windows 11 visuals and notification toasts.
- **Headless CLI** (`pgc assess --tenant … --report …`) for pipeline use, reusing `GovernanceWorkspace`.
- **Optional SQL Server** store for teams that want central evidence, implemented as additional ports.
- **Plugin SDK**: load third-party `IModule` assemblies from a signed plugin directory with a manifest and permission declaration.
- **Localization**: externalized strings, starting with the labels and report templates.

## Explicitly out of scope

- Writing to tenants without an approved, signed executor profile.
- Storing AI provider keys in the application database.
- Sending raw tenant identifiers to any AI provider.
- Supporting end-of-life Windows versions.
