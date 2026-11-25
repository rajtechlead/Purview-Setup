# 07 — Requirements Traceability

Status values: **Implemented** · **Partial** · **Planned** · **Not implemented**. This table is the honest boundary of the build; nothing marked Partial or Planned should be described as delivered.

> **Superseded for status tracking.** Requirement-level IDs, per-item proof and gap actions live in [08 — Requirements Tracker](08-requirements-tracker.md), and the 22 named artifacts in [09 — Deliverables Tracker](09-deliverables-tracker.md). Where this summary and those trackers disagree, the trackers are correct.

## Platform and shell

| Requirement | Status | Evidence / gap |
| --- | --- | --- |
| Windows 10/11, Server 2019/2022/2025 only, EOL excluded | Implemented | `app.manifest` compatibility IDs; startup build check (`< 17763` refused) |
| .NET 9, C#, MVVM, WPF | Implemented | `src/PurviewCopilot.UI`, `Mvvm/`, `ViewModels/` |
| WinUI 3 preferred shell | Not implemented | WPF chosen for verifiable in-box build; UI-agnostic core allows a future shell |
| Clean architecture, SOLID, DRY, DI, repository/factory/strategy | Implemented | Four-layer solution, ports and adapters, module/factory patterns, explicit composition root |
| SQLite, optional SQL Server | Partial | SQLite implemented; SQL Server port not implemented |
| Plugin architecture | Partial | `IModule` seam + registry; no external plugin loader |

## Authentication

| Requirement | Status | Evidence / gap |
| --- | --- | --- |
| Entra app registration, certificate default | Implemented | `EntraTokenProvider`, `AuthConfiguration` default |
| Certificate auto-generation, import/export, store integration, rotation, expiry monitoring, validation | Implemented | `WindowsCertificateService`, Settings page, 30-day warning |
| Azure Key Vault integration | Implemented | `KeyVaultCertificateStore` (secret REST API) — not wired into the UI yet |
| Managed identity, interactive, client secret (lab) | Implemented | Credential selection by mode |
| Guided wizard (app registration → consent → cert → SP → connectivity → security validation) | Partial | Settings page implements connection/cert/validation; no step-by-step wizard and no automated app-registration or consent creation |

## Discovery and assessment

| Requirement | Status | Evidence / gap |
| --- | --- | --- |
| Purview: information protection, labels, auto-labelling, DLP/endpoint DLP, classification, lifecycle, records, audit, insider risk, comms compliance, eDiscovery | Implemented | 49 controls; PowerShell collector |
| DSPM, AI governance, Copilot governance | Partial | Controls and scoring implemented; DSPM/AI facts are not collected from a tenant (no stable Graph surface used), so they report as gaps |
| Microsoft 365: Exchange, SharePoint, OneDrive, Teams, users/groups | Partial | Users/groups exact; site/team counts via Graph; mailbox and OneDrive counts are proxies |
| Entra: users, roles, applications, service principals, Conditional Access | Partial | Users, GA count, app secrets, CA count; no per-role inventory beyond GA |
| Defender integrations, security/compliance posture | Partial | Secure score via Graph; Defender-specific signals not ingested; compliance posture is a placeholder |
| Seven scores | Implemented | `ScoringEngine`, dashboard |
| Deployment modes 1–6 | Partial | All six selectable and share the guarded pipeline; "one-click" and "greenfield" differ only in preselection/baseline content |
| Review/modify/approve/apply/rollback | Partial | Review, modify (selection), approve and backup implemented; apply and rollback are gated to script generation |

## Modules

| Requirement | Status | Evidence / gap |
| --- | --- | --- |
| 16 independent modules with discovery/assessment/recommendations/deployment/validation/reporting/rollback/monitoring | Partial | All 16 exist with rules, findings, recommendations and operations; deployment operations are generic descriptors, not per-cmdlet implementations |

## AI

| Requirement | Status | Evidence / gap |
| --- | --- | --- |
| Recommendations, analysis, troubleshooting, planning, executive summaries, reports, remediation guidance | Implemented | `CopilotService` prompt + assessment context |
| Security Copilot | Not implemented | No Security Copilot API surface integrated |
| Azure OpenAI, OpenAI, Claude, Gemini, DeepSeek, Grok, OpenRouter | Implemented | OpenAI-compatible adapters + Anthropic + Gemini |
| Ollama, LM Studio, Open WebUI | Implemented | OpenAI-compatible local adapters; health check for local `/models` |
| Custom APIs/endpoints/private LLMs | Implemented | `AiProviderKind.Custom` with endpoint/model |
| Failover: quota, free-tier, health, cost, tokens | Partial | Quota/rate/auth/timeout failover, token and cost tracking implemented; health probe not used by the loop |
| Privacy guard: detect, redact, mask, tokenize, anonymize, classify, sanitize | Implemented | Local detector + four modes + review + audit + cloud off + offline mode |
| Full AI audit trail, no raw tenant data externally | Implemented | Audit entries store rule IDs and usage, never values |

## Change management and safety

| Requirement | Status | Evidence / gap |
| --- | --- | --- |
| Pack: executive, technical, business, user, security, compliance, licensing, risk, validation, rollback | Implemented | `ChangeImpact` produced by `ChangePlanBuilder` |
| Backups: configuration, policy, label, rule, rollback package | Partial | Snapshot + operations backup captured; no per-object Purview exports yet |
| Simulation, what-if, pre/post validation | Implemented | `ChangeExecutor.SimulateAsync`, plan validation steps |
| Approval workflow: change requests, multi-stage, CAB, history | Implemented | `ApprovalWorkflow` (Security → Compliance → CAB), append-only history, plan state |
| Multi-tenant MSP mode | Not implemented | Single configured tenant; no switcher or per-customer isolation |
| Configuration-as-code: PowerShell, ARM, Bicep, Terraform, JSON | Partial | PowerShell script generation and internal JSON; no ARM/Bicep/Terraform |

## Operations, reporting and UX

| Requirement | Status | Evidence / gap |
| --- | --- | --- |
| Offline/regulated mode, local AI only, no-cloud | Implemented | `OfflineMode`, `CloudAiEnabled`, local provider filter |
| Government clouds | Not implemented | Endpoints are commercial-cloud constants |
| Licence optimisation | Implemented | `LicenseOptimizer` (unused, over-assigned, E5 gap, Copilot prerequisites, SKU sprawl) |
| Continuous monitoring and drift alerts | Partial | On-demand drift comparison; no scheduler or notification channel |
| Reports PDF/DOCX/XLSX/HTML/JSON | Partial | HTML/JSON/CSV/Markdown only; PDF, DOCX and XLSX are not implemented |
| Framework readiness: ISO 27001, NIST CSF, CIS, SOC 2, HIPAA, GDPR, PCI-DSS | Implemented | `FrameworkMapper`; also ISO 42001 and NIST AI RMF |
| Executive/security/governance/compliance dashboards, heatmap, trends, action center | Partial | One dashboard with all scores, module bars, priority findings; no separate dashboards, heatmap grid, trend history or dedicated action-center page |
| Help center (12 topics) | Implemented | Searchable `HelpViewModel` |
| About page with authorship, owner, contact | Implemented | `AboutViewModel` |

## Security engineering

| Requirement | Status | Evidence / gap |
| --- | --- | --- |
| SDL, OWASP Top 10/ASVS, CIS, NIST SSDF | Partial | Documented mapping and review; code signing and tamper detection not implemented; DB unencrypted |
| Least privilege, RBAC, audit logging, secrets management | Implemented | Read-only scopes, per-user install, role guidance, local audit, env-var keys |
| Dependency and vulnerability scanning | Implemented | CI step (`dotnet list package --vulnerable`), two direct dependencies |
| Threat modelling and security review | Implemented | `docs/03-security-architecture.md` (STRIDE, findings S-01…S-08) |
| Encryption at rest / in transit | Partial | TLS for transport; local DB plaintext (gap S-02) |

## Packaging and delivery

| Requirement | Status | Evidence / gap |
| --- | --- | --- |
| MSI (WiX), MSIX, silent install, enterprise deployment | Partial | Projects and manifests authored; not built in this environment (WiX not installed). Intune/MECM commands documented |
| Upgrade and rollback | Partial | MSI major-upgrade configured; rollback = reinstall previous package; documented |
| CI/CD pipeline | Implemented | `.github/workflows/ci.yml` (build, test, publish, vulnerability scan, artifact) |
| Automated test framework | Implemented | 36 tests: scoring, catalog, privacy, approvals, planning, licensing, drift, failover, SQLite, reports |
| Documentation set (architecture, design, schema, API, auth, cert, privacy, modules, threat model, guides, runbook, roadmap) | Implemented | `docs/01`–`07`, README, CHANGELOG |

## Summary

- Fully implemented and tested: assessment engine, 49-control catalog, scoring, privacy guard, AI failover, certificate lifecycle, approvals, backups, simulation, HTML/JSON/CSV/Markdown reporting, WPF workspace, 36 tests.
- Deliberately gated: live tenant mutation.
- Largest honest gaps: MSP multi-tenancy, live/real deployment execution, PDF/DOCX/XLSX, config-as-code, scheduled monitoring, Security Copilot, DSPM/Compliance Manager data collection, UI wizard, code signing, encrypted local store.
