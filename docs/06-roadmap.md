# 06 — Future Roadmap

Ordered by risk reduction and by what the current build deliberately leaves out.

## Phase 1.1 — Close the safety gaps (next)

| Item | Why | Notes |
| --- | --- | --- |
| Encrypted local store | Assessment evidence currently sits in a plaintext SQLite file | SQLCipher or a DPAPI-protected key; migrate existing DB in place |
| Signed builds and update channel | Enterprise deployment requires Authenticode | CI signing step, SHA-256 manifest, optional winget/Intune channel |
| Hash-chained audit log | Local DB is user-writable | Each audit row stores the hash of the previous row; verification command |
| Provider health pre-check | `IsHealthyAsync` exists but the failover loop does not consult it | Circuit breaker with cooldown per provider |
| Per-user secret store for lab mode | Client secret currently lands in the settings document | DPAPI (`ProtectedData`) or Windows Credential Manager |

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
