# 06 — Future Roadmap

Ordered by risk reduction and by what the current build deliberately leaves out.

## Phase 1.1 — Close the safety gaps

**4 complete, 1 partial.** Updated 25 November 2026.

| Item | Status | Why | Notes |
| --- | --- | --- | --- |
| Encrypted local store | **Done (payload-level)** | Assessment evidence sat in a plaintext SQLite file | Assessment, change-plan, approval and backup payloads are encrypted with DPAPI (`CurrentUser`) before they reach the file and decrypted on read; legacy plaintext rows still load and are encrypted on their next save. Tenant ids, timestamps and scores stay in columns for indexed queries, so SQLite pages and that metadata remain plaintext — SQLCipher page encryption is still open. Test: `AssessmentEvidence_IsEncryptedAtRest_AndStillLoads`. |
| Signed builds and update channel | **Partial** | Enterprise deployment requires Authenticode | Settings → Updates checks an https manifest (`version`, `downloadUrl`, `sha256`), rejects non-https URLs and malformed hashes, and only then downloads the MSI — the file is written solely when its SHA-256 matches, and installation stays an explicit operator action. Still open: Authenticode signing (no certificate here), a published manifest, and automatic install. |
| Hash-chained audit log | **Done** | Local DB is user-writable | Each row stores the SHA-256 of its canonical fields plus the previous hash, in insertion order. Legacy rows are backfilled on first open; `IAuditLog.VerifyAsync` recomputes the chain; Settings has a **Verify audit log integrity** button. Tamper detection is covered by tests. |
| Provider health pre-check | **Done** | `IsHealthyAsync` existed but the failover loop did not consult it | Every candidate is probed with a 5-second timeout. Unhealthy providers are skipped and recorded as `Unhealthy`, and any failure puts that provider in a 2-minute cooldown so the next call goes straight to the fallback. |
| Per-user secret store for lab mode | **Done** | Client secret landed in the settings document | `DpapiSecretProtector` uses crypt32 DPAPI with `CurrentUser` scope, so no key material sits beside the data and another profile cannot decrypt it. Values are transparently re-encrypted on save and decrypted on load; a profile change yields an empty secret rather than a crash. |

## Phase 1.2 — Deployment depth

**3 of 4 complete, 1 partial.** Updated 26 November 2026.

- **Live executor profile** — **Done (unverified live)**. `PowerShellDeploymentExecutor` runs only after a session opt-in and a passing safety gate (simulation, per-phase backup, approval, rollback package, authentication, connectivity, risk), with certificate auth and no secrets in the script. Off at every start; no live tenant was available to verify post-conditions.
- **Real operation payloads** — **Done**. `PurviewScriptLibrary` maps twelve controls to the cmdlets that actually configure them: `New-Label`, `New-LabelPolicy`, `New-AutoSensitivityLabelPolicy`/`Rule`, `New-DlpCompliancePolicy`/`Rule`, `Set-DlpCompliancePolicy`, `New-ComplianceTag`, `New-RetentionCompliancePolicy`/`Rule`, `Set-AdminAuditLogConfig` and `New-UnifiedAuditLogRetentionPolicy`. Every block is idempotent and checks current state first. Controls with no reliable cmdlet (DSPM, Endpoint DLP onboarding, Compliance Manager, AI governance, eDiscovery cases) are emitted as explicit `MANUAL` steps with the verification command instead of guessed syntax.
- **Rollback execution** — **Partial**. A reversal script is generated in reverse operation order with destructive commands deliberately commented out and the pre-change backup named as the source of truth. Executing the restore and comparing post-rollback scores still requires the live executor.
- **Configuration-as-code** — **Done**. Change plans export as a portable JSON package and as PowerShell, and import back with a new plan id that forces simulation and approval again. Deployment also exports existing labels, label/auto-label policies and rules, DLP, retention and compliance tags to JSON, and saves ARM, Bicep and Terraform templates for the Purview account (compliance policy is not ARM-deployable, so it stays JSON/PowerShell).

## Phase 1.2A — Autonomous authentication and safe deployment

**In progress.** Updated 26 November 2026.

- **Authentication Provisioning Center** — **Done (unverified live)**. Guided/one-click setup creates the app registration, enterprise application and service principal, certificate (RSA 2048/3072/4096 or ECC P-256/P-384; 6–120 months) or lab-only secret, owners, Graph role assignments and admin consent, then validates the token and organisation read. Dry run, mandatory separation of duties, 10-year acknowledgement, audit steps and a rollback script are included. No sovereign or commercial tenant was available to exercise the Graph calls.
- **Certificate lifecycle** — **Done**. Generation/install/export/rotation plus ECDSA support, expiration warnings and public-key upload to the app registration.
- **Client secret automation** — **Done**. Lab/test only, 3/6/12/24 months, DPAPI at rest through the settings store, with the production warning retained.
- **Safe deployment execution and phased rollout** — **Done (unverified live)**. A six-phase plan (discovery, audit/monitoring, information protection, DLP, records, insider risk/AI) runs one phase at a time: simulate, per-phase backup, separation-of-duties approval, gated live execution, automatic post-deployment validation, and a blocked next phase until the previous one validates. Destructive phases need explicit risk acceptance.
- **Backup depth** — **Done (unverified live)**. Phase backups contain the snapshot and phase operations; **Back up tenant objects** additionally stores the real label, label-policy, auto-label, DLP, retention and compliance-tag export as an encrypted `objects/` backup.
- **Rollback execution** — **Partial**. Phase rollback marks the phase, names the backup and generates the reviewed reversal script, but there is no automated restore or post-restore re-read.

## Phase 1.3 — Reporting and multi-tenant

Platform floor: **Done**. The manifest advertises Windows 10/11 only and startup refuses builds below 17763, so Windows 7/8/8.1 cannot run the app.

Installer rollback: **Done**. `Rollback-PurviewGovernanceCopilot.ps1` ships beside the app and restores a named previous MSI, with `-WhatIf`, version verification and msiexec logs.

Plugin modules: **Done**. `PluginModuleLoader` discovers external `IModule` assemblies from the user's plugins folder, isolates load failures and registers them as custom modules; the desktop and CLI both use the extended registry.

Deployment modes: **Done**. `DeploymentModePolicy` gives each mode its own starting set and behaviour — Assessment Only cannot plan, One-Click builds the recommended plan automatically, Expert selects everything, Custom selects nothing — while every plan still passes simulation and approvals.

Greenfield path: **Done**. `GreenfieldBaseline` detects an empty tenant from collected counts and selects a first-run foundation set in a safe order; the same mode on a configured tenant warns rather than claiming a baseline.

**3 of 4 complete, 1 partial.** Updated 26 November 2026.

- **Audience report types** — **Done**. Reports offers Full, Executive, Governance, Security, Compliance and Remediation profiles; HTML/PDF/Markdown filter findings and recommendations, and JSON carries the selected type.
- **PDF, DOCX and XLSX** — **Done**. Dependency-free writers: a paginated PDF 1.4 document with base-14 fonts, and OOXML packages built directly with `ZipArchive` for DOCX (single document part) and XLSX (Scores, Findings, Frameworks sheets). Exported from Reports; covered by five tests including escaping and package validity.
- **Risk heatmap, trends and persona dashboards** — **Done**. The dashboard has Executive, Security, Governance and Compliance tabs; the Security tab carries a module × severity heatmap of open findings; Executive charts the last twelve overall scores from stored assessments and shows the maturity trail and delta. Monitoring exports a Markdown trend report with overall/security/governance/compliance/risk direction.
- **Multi-tenant MSP mode** — **Done**. Customer tenants persist in `tenant_profiles` with DPAPI-protected credentials; Settings saves/activates a tenant and clears in-session evidence. Per-customer access assignments (Viewer/Operator/Approver/Administrator) restrict which tenants appear, who may activate or edit them, and who may operate or approve a deployment. The Dashboard Portfolio and CSV export remain. Tests: `TenantAccessTests`.
- **Licence cost model** — **Done**. Settings accepts optional monthly prices by SKU part number; unused and over-assigned seats on Monitoring then show a monthly dollar opportunity, while discovery still reports exact utilisation when no price is set. Tests: `LicensePriceTests`.
- **Scheduled monitoring** — **Partial**. Monitoring has an in-app schedule (`MonitoringIntervalMinutes`, minimum 15 minutes, off by default) that re-runs the read-only assessment and rebuilds drift, alerts and licence signals; **Check now** runs the same path. Alerts are generated locally from drift, score drops and critical failures, listed on the Monitoring page, and can be delivered to an operator-supplied HTTPS webhook (Teams/Slack incoming webhooks work) either on demand or after a scheduled check. **Save Task Scheduler script** generates a PowerShell file that registers a daily headless `pgc assess` job and writes timestamped HTML reports, so checks are not limited to an open window. There is no Windows service or native email/Teams connector yet.

## Phase 2 — AI and compliance depth

- **Microsoft Security Copilot** integration where a tenant-licensed endpoint is available.
- **Deeper AI governance**: prompt/response classification history, model inventory, ISO 42001 control mapping, agent governance.
- **Compliance Manager API**: real improvement-action ingestion and score instead of a placeholder posture value.
- **Defender integration**: exposure management signals and device risk feeding endpoint DLP readiness.
- **Government clouds** — **Done**. A Cloud selector on the connection (`AuthConfiguration.Cloud`) routes the Entra authority and Graph scope, Graph v1.0/beta base URLs, the connection test and Security & Compliance PowerShell to commercial/GCC, GCC High (`graph.microsoft.us`) or DoD (`dod-graph.microsoft.us`, L5 compliance). Endpoints were checked against Microsoft Learn; no sovereign tenant was available to exercise them.
- **Custom sensitive information types**: author and validate custom SITs as part of deployment.

## Phase 3 — Platform

- **Action centre** — **Done**. Assessment assigns an owner, due date, status (Open/InProgress/Blocked/Done) and notes to every open finding; records persist per tenant in `finding_actions`, notes are DPAPI-encrypted at rest, and the list shows how many findings have an assignment. Tests: `FindingActionTests`.

- **WinUI 3 shell** consuming the same Application layer, with Windows 11 visuals and notification toasts.
- **Headless CLI** — **Done**. `src/PurviewCopilot.Cli` builds `pgc` and reuses `GovernanceWorkspace`, the same SQLite evidence store and exporters. Commands: `pgc assess` (certificate or lab secret, or `--demo` for the sample tenant) with `--format`, `--out`, `--mode`, `--org`, `--by`, `--data-dir`; and `pgc verify-audit`. It is read-only, exits non-zero on failure and was run end to end with the demo tenant (JSON report plus a valid audit chain).
- **Optional SQL Server** store for teams that want central evidence, implemented as additional ports.
- **Plugin SDK**: load third-party `IModule` assemblies from a signed plugin directory with a manifest and permission declaration.
- **Localization**: externalized strings, starting with the labels and report templates.

## Explicitly out of scope

- Writing to tenants without an approved, signed executor profile.
- Storing AI provider keys in the application database.
- Sending raw tenant identifiers to any AI provider.
- Supporting end-of-life Windows versions.
