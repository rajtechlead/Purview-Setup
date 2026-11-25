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

**2 of 4 complete, 1 partial.** Updated 25 November 2026.

- **Live executor profile** — **Open**. Needs a separately installed, signed executor component and a live tenant to verify post-conditions. Apply remains gated to script generation.
- **Real operation payloads** — **Done**. `PurviewScriptLibrary` maps twelve controls to the cmdlets that actually configure them: `New-Label`, `New-LabelPolicy`, `New-AutoSensitivityLabelPolicy`/`Rule`, `New-DlpCompliancePolicy`/`Rule`, `Set-DlpCompliancePolicy`, `New-ComplianceTag`, `New-RetentionCompliancePolicy`/`Rule`, `Set-AdminAuditLogConfig` and `New-UnifiedAuditLogRetentionPolicy`. Every block is idempotent and checks current state first. Controls with no reliable cmdlet (DSPM, Endpoint DLP onboarding, Compliance Manager, AI governance, eDiscovery cases) are emitted as explicit `MANUAL` steps with the verification command instead of guessed syntax.
- **Rollback execution** — **Partial**. A reversal script is generated in reverse operation order with destructive commands deliberately commented out and the pre-change backup named as the source of truth. Executing the restore and comparing post-rollback scores still requires the live executor.
- **Configuration-as-code** — **Partial**. Change plans export as a portable JSON package and as PowerShell, and import back with a new plan id that forces simulation and approval again. Export of the tenant's existing label/DLP/retention objects and ARM/Bicep/Terraform coverage remain open.

## Phase 1.3 — Reporting and multi-tenant

**2 of 4 complete.** Updated 25 November 2026.

- **PDF, DOCX and XLSX** — **Done**. Dependency-free writers: a paginated PDF 1.4 document with base-14 fonts, and OOXML packages built directly with `ZipArchive` for DOCX (single document part) and XLSX (Scores, Findings, Frameworks sheets). Exported from Reports; covered by five tests including escaping and package validity.
- **Risk heatmap, trends and persona dashboards** — **Done**. The dashboard has Executive, Security, Governance and Compliance tabs; the Security tab carries a module × severity heatmap of open findings; Executive charts the last twelve overall scores from stored assessments and shows the maturity trail and delta.
- **Multi-tenant MSP mode** — **Open**. Needs a tenant switcher, explicit per-customer isolation, a cross-tenant dashboard and per-customer roles.
- **Scheduled monitoring** — **Open**. Needs a scheduler and an alert channel; drift is currently on-demand.

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
