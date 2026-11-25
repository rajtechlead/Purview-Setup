# 08 — Requirements Tracker

Requirement-level tracker for the **Purview Governance Copilot** specification. This document is the authoritative answer to "is everything delivered?" — it is deliberately stricter than the README and supersedes [07 — Requirements Traceability](07-requirements-traceability.md) where they differ.

Verified against commit `353bde2` on 25 November 2026 by reading the source, counting catalog entries and running the test suite. Nothing below is marked **Delivered** on the strength of a document alone.

## How to read this

**Status**

| Status | Meaning |
| --- | --- |
| **Delivered** | The requirement is met by working code, by a shipped artifact, or by a documented procedure that exists in the repository. |
| **Partial** | Something real exists, but a named part of the requirement is absent, unwired, or only advisory. |
| **Not delivered** | Nothing in the repository implements the requirement. |

**Proof** (how the claim was checked)

| Code | Meaning |
| --- | --- |
| **T** | Covered by an automated test (80 tests, all passing at audit time). |
| **B** | Builds and is reachable from the composition root; behaviour not exercised end to end. |
| **C** | Code exists but is unexercised here (no tenant, certificate, API key or admin rights). |
| **D** | Documentation or project/manifest artifact only; nothing executes it here. |

Status and proof are independent: a requirement can be **Delivered** with proof **C** when the code is complete but cannot be verified without a live tenant.

---

## A. Platform, scope and objective

| ID | Requirement | Status | Proof | Evidence / gap |
| --- | --- | --- | --- | --- |
| PLT-01 | Windows 10/11 and Server 2019/2022/2025 only; EOL operating systems excluded | **Partial** | B | `App.xaml.cs` refuses builds below 17763 (Server 2019 / Win10 1809). `app.manifest` still declares Windows 7, 8 and 8.1 `supportedOS` GUIDs, contradicting the stated floor. Action: reduce the manifest to the Win10/11 GUID and add explicit Server 2019/2022/2025 compatibility notes. |
| PLT-02 | Single-pane workspace covering assessment, deployment, governance, monitoring, reporting, optimisation and lifecycle | **Delivered** | B | Nine pages: Dashboard, Assessment, Deployment, Monitoring, Reports, AI Assistant, Settings, Help, About (`src/PurviewCopilot.UI/Views`). |
| PLT-03 | Support both greenfield and existing deployments | **Partial** | B | All six modes exist, but Greenfield differs only in recommendation preselection; there is no distinct first-run baseline or empty-tenant path. |
| PLT-04 | Design grounded in Microsoft Learn, Purview, Entra, Graph, Security Copilot, CIS, NIST, OWASP, ISO 27001, SOC 2, HIPAA, GDPR and PCI-DSS guidance | **Delivered** | D | Framework references are attached per control (`ModuleCatalog.cs`) and reviewed in `docs/03`. No in-app live retrieval of Microsoft guidance. |

## B. Authentication

| ID | Requirement | Status | Proof | Evidence / gap |
| --- | --- | --- | --- | --- |
| AUT-01 | Entra app registration with certificate authentication as the default | **Delivered** | C | `EntraTokenProvider` builds `ClientCertificateCredential`; certificate is the default `AuthMode`. App registration itself is a documented manual step (`docs/05` §3). |
| AUT-02 | Certificate auto-generation | **Delivered** | C | `WindowsCertificateService.CreateSelfSigned` (4096-bit RSA, CurrentUser\My), invoked from Settings. |
| AUT-03 | Certificate import and export (PFX) | **Delivered** | C | `ImportFromPfx` / `ExportToPfx` plus Settings commands. |
| AUT-04 | Azure Key Vault integration | **Partial** | C | `KeyVaultCertificateStore` (secret REST upload/download) exists but is not constructed in `AppServices.Create`, has no UI, and is untested. Action: wire it behind the certificate panel and add a round-trip test. |
| AUT-05 | Windows Certificate Store integration | **Delivered** | C | Install, remove, list and `FindByThumbprint` against CurrentUser\My. |
| AUT-06 | Certificate rotation | **Delivered** | C | `Rotate` creates a replacement and returns the new thumbprint; the operator must update the app registration, as prompted in the UI. |
| AUT-07 | Expiration monitoring | **Delivered** | B | Settings surfaces certificates expiring inside 30 days (`CertificateExpiryWarningDays`). Warning-only; no scheduled notification. |
| AUT-08 | Automated validation | **Partial** | C | Thumbprint validation and an organisation/Graph connection test exist. No automated validation that the app registration, permissions or admin consent are correct. |
| AUT-09 | Managed identity (secondary) | **Delivered** | B | `AuthMode.ManagedIdentity`, system- or user-assigned client id. |
| AUT-10 | Interactive sign-in (secondary) | **Delivered** | B | `InteractiveBrowserCredential` with the loopback redirect. |
| AUT-11 | Client secret, lab/testing only (secondary) | **Delivered** | B | `AuthMode.ClientSecret`; the secret is DPAPI-protected in the settings document and in tenant profiles, and resolves from memory at call time. |
| AUT-12 | Guided wizard: app registration → permissions → consent → certificate → service principal → connectivity → security validation | **Partial** | D | Settings provides certificate and connection actions; `docs/05` §3 is a manual checklist. There is no stepped wizard, no app-registration creation, no consent automation, no service-principal creation and no security-validation step. |

## C. Discovery and assessment

| ID | Requirement | Status | Proof | Evidence / gap |
| --- | --- | --- | --- | --- |
| DSC-01 | Discover Information Protection, sensitivity labels and auto-labelling | **Delivered** | C | `Get-Label`, `Get-LabelPolicy`, `Get-AutoSensitivityLabelRule` in the embedded read-only PowerShell collector. |
| DSC-02 | Discover DLP and Endpoint DLP | **Delivered** | C | `Get-DlpCompliancePolicy`; endpoint state is derived from DLP/device evidence. |
| DSC-03 | Discover data classification | **Delivered** | C | Label, auto-label and trainable-classifier signals feed `CLS-01/02`. |
| DSC-04 | Discover data lifecycle and records management | **Delivered** | C | `Get-RetentionCompliancePolicy`, `Get-ComplianceTag`, disposition-review and regulatory-record signals. |
| DSC-05 | Discover audit configuration | **Delivered** | C | `Get-AdminAuditLogConfig`, `Get-UnifiedAuditLogRetentionPolicy`, `Get-ProtectionAlert`. |
| DSC-06 | Discover insider risk and communication compliance | **Delivered** | C | `Get-InsiderRiskPolicy`, `Get-SupervisoryReviewPolicyV2`. |
| DSC-07 | Discover eDiscovery | **Delivered** | C | `Get-ComplianceCase`; advanced eDiscovery capacity is inferred, not queried directly. |
| DSC-08 | Discover DSPM and AI governance | **Partial** | C | Controls DSP-01…03 and AIG-01…03 exist, but DSPM and DSPM-for-AI enablement are not collected from any stable API, so they report as failures by default. |
| DSC-09 | Discover Exchange Online, SharePoint Online, OneDrive, Teams, users and groups | **Partial** | C | Users, groups and site/team counts come from Graph. Mailbox and OneDrive counts are approximations from enabled users, and the collector says so in its evidence text. |
| DSC-10 | Discover Entra users, roles, applications, service principals and Conditional Access | **Partial** | C | Users, Global Administrator count, app registrations with secrets and Conditional Access policy count are collected. No role inventory beyond GA, no service-principal inventory, no per-policy CA detail. |
| DSC-11 | Discover Defender integrations, security posture and compliance posture | **Partial** | C | Secure score is read from Graph. Defender product signals are not ingested and the Compliance Manager score is not collected, so `CPM-01` warns on an absent value. |
| DSC-12 | Produce Security, Governance, Compliance, Purview Maturity, Risk, AI Governance and Copilot Readiness scores | **Delivered** | T | `ScoringEngine` + `ScoreSet` (seven scores plus maturity band); weighted modules, capped risk. Tests: `Compute_*`, `Risk_*`. |
| DSC-13 | Assess all sixteen module areas automatically | **Delivered** | T | 49 `ControlRule`s in `ModuleCatalog` across 16 `StandardModule`s; `Catalog_CoversEveryModule_WithAtLeastTwoControls` enforces coverage. |
| DSC-14 | Evidence-based findings with remediation guidance | **Delivered** | T | Every rule emits pass/warn/fail with evidence text, remediation, framework references and licence requirements. |

## D. Deployment modes and change control

| ID | Requirement | Status | Proof | Evidence / gap |
| --- | --- | --- | --- | --- |
| DEP-01 | Six deployment modes (assessment, guided, one-click, custom, expert, greenfield) | **Delivered** | B | `DeploymentMode` enum and the Deployment page mode selector expose all six. |
| DEP-02 | Modes behave materially differently | **Partial** | T | All modes share one guarded pipeline; only recommendation preselection and baseline content differ. One-click and guided are not distinct workflows. |
| DEP-03 | Review, modify, approve, apply and roll back | **Partial** | T | Review, modify, three-stage approval and backup are real. Apply is gated to script generation; a reversal script is generated in reverse order with destructive commands commented out, but no restore is executed. |
| DEP-04 | One-click recommended deployment | **Partial** | B | A plan can be built from preselected high/critical recommendations in one action, but it still requires simulation and three approvals before a script is produced. |
| DEP-05 | Apply changes automatically to the tenant | **Not delivered** | B | `ChangeExecutor(allowLiveExecution: false)` is hard-coded in `AppServices`; `ApplyAsync` returns "blocked by configuration". This is a deliberate safety decision, but the requirement as written is unmet. |

## E. Modular architecture

| ID | Requirement | Status | Proof | Evidence / gap |
| --- | --- | --- | --- | --- |
| MOD-01 | Sixteen independent modules | **Delivered** | T | `ModuleRegistry` builds all 16 with descriptors, licences, frameworks and weights; `Registry_ExposesSixteenModules_AndWeightsArePositive`. |
| MOD-02 | Every module supports discovery, assessment, recommendations, deployment, validation, reporting, rollback and monitoring | **Partial** | B | Discovery, assessment, recommendations and reporting are per-module. Deployment operations are generic descriptors (`Purview/<module>/<control>`), validation is advisory text per plan, and rollback/monitoring are tenant-wide rather than per module. |
| MOD-03 | Plugin architecture | **Partial** | B | `IModule` + registry is a genuine extension seam, but there is no external plugin discovery or loading. |

## F. AI assistant and failover

| ID | Requirement | Status | Proof | Evidence / gap |
| --- | --- | --- | --- | --- |
| AIA-01 | AI for recommendations, security/governance/compliance analysis, troubleshooting, deployment planning, executive summaries, reports, risk analysis and remediation guidance | **Delivered** | B | `CopilotService` composes a system prompt and sanitized assessment context covering these tasks. |
| AIA-02 | Microsoft Security Copilot integration | **Not delivered** | — | No Security Copilot API surface is implemented. |
| AIA-03 | Azure OpenAI, OpenAI, Claude, Gemini, DeepSeek, Grok and OpenRouter | **Delivered** | B | OpenAI-compatible adapter plus native Anthropic and Gemini adapters. Presets ship for Azure OpenAI, OpenAI, Claude and Gemini; DeepSeek, Grok and OpenRouter are configured by endpoint/model through the custom OpenAI-compatible path. |
| AIA-04 | Local AI: Ollama, LM Studio, Open WebUI | **Delivered** | B | Ollama preset with loopback `/models` health check; LM Studio and Open WebUI via OpenAI-compatible local endpoints. |
| AIA-05 | Custom APIs, custom endpoints and private LLMs | **Delivered** | B | `AiProviderKind.Custom` and any OpenAI-compatible base URL. |
| AIA-06 | Failover on quota and free-tier exhaustion | **Delivered** | T | 402/429 classified as `QuotaExhausted` or `RateLimited` and the next provider is tried; `Failover_MovesToNextProvider_OnQuotaFailure`. Detection is response-based, not balance-aware. |
| AIA-07 | Provider health monitoring | **Delivered** | T | Every candidate is probed with a 5-second timeout before dispatch; unhealthy providers are skipped, recorded as `Unhealthy`, and cooled down for 2 minutes. Tests: `Failover_SkipsUnhealthyProvider`, `Failover_CoolsDownFailedProvider_OnNextCall`. |
| AIA-08 | Cost tracking | **Delivered** | T | Per-call cost from input/output token rates is persisted; `Failover_ComputesCostFromTokenRates`. |
| AIA-09 | Token tracking | **Delivered** | T | Prompt/completion tokens recorded per attempt, including failures. |
| AIA-10 | Automatic switch to the next approved provider and continue | **Delivered** | T | Priority-ordered loop with failure classification and usage records. |
| AIA-11 | Provider configuration in the UI | **Delivered** | T | Settings lists providers with enable flags, endpoints, models and priority; the operator moves each provider up or down and the sequence persists as priorities 1..n, which is exactly the orchestrator's auto-switch order. Tests: `ProviderSequenceTests`. |
| AIA-12 | Quota, spend and usage visibility | **Delivered** | T | Monitoring's AI usage ledger aggregates every stored row in SQL (`COUNT`/`SUM`/`COUNT DISTINCT`, no listing limit) and reports request count, prompt/completion tokens, success rate, failures, distinct providers and estimated spend (explicitly not a provider invoice). Tests: `UsageTotals_CoverEveryRowBeyondTheListingLimit`, `AiUsageSummary_AggregatesTokensCostsAndFailures`. |

## G. AI privacy guardrail (mandatory)

| ID | Requirement | Status | Proof | Evidence / gap |
| --- | --- | --- | --- | --- |
| PRV-01 | Detect PII | **Delivered** | T | Email, phone, SSN, payment card (Luhn-validated), IBAN. |
| PRV-02 | Detect sensitive information | **Delivered** | T | SSN, card, IBAN and contextual patterns; overlap de-duplication tested. |
| PRV-03 | Detect secrets and credentials | **Delivered** | T | JWT, bearer token, AWS access key, PEM private key, connection-string secret, `password=` style assignments. |
| PRV-04 | Detect tenant identifiers | **Delivered** | T | Tenant GUID and `*.onmicrosoft.com` domains; SharePoint URLs. |
| PRV-05 | Detect customer information | **Partial** | T | Detects tenant domain, GUID and SharePoint URL, but no person/company-name or free-text customer-data classifier. |
| PRV-06 | Redaction, masking, tokenization and anonymization | **Delivered** | T | Four modes; tokens are stable and locally reversible; masking retains only the last four characters. |
| PRV-07 | Classification and prompt sanitization | **Delivered** | T | Matches are classified by kind and the prompt is rebuilt from sanitized text before send. |
| PRV-08 | No raw tenant data, PII or sensitive configuration sent externally | **Delivered** | T | `CopilotService.PrepareAsync` gates every outbound prompt; assessment context is sanitized with the same detector. Blocked prompts never reach a provider. |
| PRV-09 | Full administrator review option | **Delivered** | B | `RequirePromptReview` (default on) returns the sanitized prompt for approval before `SendAsync`. |
| PRV-10 | Complete AI audit trail | **Delivered** | T | Every decision and provider attempt is written to `ai_usage` / `audit_log`, storing identifiers and kinds, never matched values. `ScrubForLog` tested. |
| PRV-11 | Cloud AI disable option | **Delivered** | B | `CloudAiEnabled` filters non-local providers out of the candidate list. |
| PRV-12 | Offline AI support | **Delivered** | B | `OfflineMode` restricts dispatch to local providers; application runs without internet access apart from discovery. |

## H. Change management and configuration safety

| ID | Requirement | Status | Proof | Evidence / gap |
| --- | --- | --- | --- | --- |
| CHG-01 | Change pack: executive, technical, business, user, security, compliance, licensing impacts, risk, validation and rollback | **Delivered** | T | `ChangeImpact` carries all ten sections; `ChangePlan_DeduplicatesOperations_AndBuildsImpact`. |
| CHG-02 | Risk analysis, validation plan and rollback plan per change | **Delivered** | T | Risks, validation steps and rollback steps are generated per plan. |
| SAF-01 | Configuration, policy, label and rule backups | **Partial** | T | `BackupService` stores a tenant snapshot plus the operation payload. No per-object export of labels, DLP policies, rules or retention labels. |
| SAF-02 | Full rollback packages | **Partial** | T | A rollback package is stored and validated, and a reversal script is generated from the plan; no automated restore is executed. |
| SAF-03 | Policy simulation mode | **Delivered** | T | `SimulateAsync` validates every operation payload and returns the exact script that would run; empty payloads fail simulation. |
| SAF-04 | What-if analysis | **Delivered** | B | Simulation reports per-operation validation and post-checks before approval. |
| SAF-05 | Pre-deployment and post-deployment validation | **Partial** | B | Pre-flight payload validation is real. Post-deployment validation is a checklist in the plan, not an automated re-read of tenant state. |

## I. Multi-tenant MSP mode

| ID | Requirement | Status | Proof | Evidence / gap |
| --- | --- | --- | --- | --- |
| MSP-01 | Manage multiple customer tenants with isolation and a central dashboard | **Delivered** | T | `tenant_profiles` stores each customer tenant with DPAPI-protected credentials; Settings saves and activates a tenant, activation clears in-session evidence, and the Dashboard Portfolio tab summarises each tenant's latest assessment. Test: `TenantProfiles_RoundTrip_WithProtectedSecrets`. |
| MSP-02 | Cross-tenant reporting | **Partial** | T | Portfolio rows aggregate overall, security, compliance, risk, maturity and last-assessed date per tenant. No combined PDF/DOCX/XLSX export across tenants. Test: `Portfolio_UsesEachTenantsOwnLatestAssessment`. |
| MSP-03 | Role-based administration and customer-level permissions | **Not delivered** | — | The app has no user model or roles; all saved tenants are reachable by the signed-in Windows user. RBAC exists only as tenant guidance in CPM controls. |

## J. Configuration-as-code

| ID | Requirement | Status | Proof | Evidence / gap |
| --- | --- | --- | --- | --- |
| CAC-01 | Export configuration as PowerShell | **Delivered** | T | `PurviewScriptLibrary` emits real, idempotent cmdlets for twelve controls and explicit `MANUAL` steps with verification commands for the rest. Exported from Deployment as `.ps1`. Tests: `Script_UsesTheRealCmdlet_ForMappedControls`, `Script_MarksUnmappedControlsAsManual_AndNeverGuesses`. |
| CAC-02 | Export as ARM, Bicep and Terraform where applicable | **Not delivered** | — | No templates in the repository. Note: Purview policy is not ARM-deployable, so this should be scoped to the Entra/Graph subset before implementation. |
| CAC-03 | JSON export and import | **Delivered** | T | `ConfigurationPackage` exports a versioned plan envelope and imports it with validation, a fresh plan id and forced re-approval. Assessment JSON export is separate. Tests: `ConfigurationPackage_RoundTripsOperations`, `ConfigurationPackage_RejectsPlanWithoutOperations`. |

## K. Approval workflow

| ID | Requirement | Status | Proof | Evidence / gap |
| --- | --- | --- | --- | --- |
| APR-01 | Change requests, multi-stage approvals, CAB approval | **Delivered** | T | Draft → Security → Compliance → CAB → Approved, with reject and request-changes paths. |
| APR-02 | Audit tracking and approval history | **Delivered** | T | Append-only history with stage, decision, actor, comment and timestamp, persisted in SQLite. |

## L. Offline and regulated mode

| ID | Requirement | Status | Proof | Evidence / gap |
| --- | --- | --- | --- | --- |
| OFF-01 | Offline deployments for regulated and isolated environments | **Delivered** | B | Self-contained publish, local SQLite, no telemetry, self-contained HTML reports, local AI only. |
| OFF-02 | Local-AI-only and no-cloud operation modes | **Delivered** | B | `OfflineMode` + `CloudAiEnabled`; every cloud call path is filtered. |
| OFF-03 | Government clouds (GCC, GCC High, DoD) | **Not delivered** | — | Graph and commercial endpoints are constants; no cloud selector or sovereign endpoints. |

## M. Licence optimisation

| ID | Requirement | Status | Proof | Evidence / gap |
| --- | --- | --- | --- | --- |
| LIC-01 | Analyse utilisation, unused licences, overspend and missing prerequisites | **Partial** | T | Exact utilisation, unused seats, over-assignment, E3→E5 gaps, Copilot prerequisites and SKU sprawl. Monetary opportunity is always 0 because no price list is embedded. |
| LIC-02 | Optimisation recommendations | **Delivered** | T | `LicenseOptimizer` insights with severity, rationale and action; surfaced on Monitoring. |

## N. Continuous monitoring

| ID | Requirement | Status | Proof | Evidence / gap |
| --- | --- | --- | --- | --- |
| MON-01 | Monitor governance, security, compliance and configuration drift | **Delivered** | T | `DriftDetector` compares a stored baseline with a fresh collection across all four kinds (11 rules). |
| MON-02 | Generate alerts | **Partial** | T | `AlertBuilder` generates local alerts from drift, score regressions (≤ −5 points) and critical failures; Monitoring lists them with severity. No external channel (email, Teams, webhook, toast). Test: `MonitoringAlertTests`. |
| MON-03 | Trend reports | **Partial** | T | Assessment history is stored per tenant; no trend chart, delta view or time-series report. |
| MON-04 | Continuous (scheduled) monitoring | **Partial** | B | In-app `DispatcherTimer` re-runs the read-only assessment every N minutes (minimum 15, off by default) and refreshes drift, alerts and licence signals; only runs while the window is open. No Windows service or Task Scheduler job. |

## O. Reporting

| ID | Requirement | Status | Proof | Evidence / gap |
| --- | --- | --- | --- | --- |
| REP-01 | PDF export | **Delivered** | T | `PdfReportExporter` writes a paginated PDF 1.4 document with Helvetica/Courier base fonts, escaped text, page footers and a valid xref. Tests: `Pdf_ProducesAWellFormedDocumentWithTheReportText`, `Pdf_EscapesParenthesesInContent`. |
| REP-02 | DOCX and XLSX export | **Delivered** | T | `DocxReportExporter` and `XlsxReportExporter` build OOXML packages directly with `ZipArchive` (no package dependency); XLSX has Scores, Findings and Frameworks sheets. Tests validate the zip entries and cell content. |
| REP-03 | HTML and JSON export | **Delivered** | T | Self-contained HTML (escaped, no external assets) and structured JSON; CSV and Markdown also ship. |
| REP-04 | Executive, governance, security, compliance and remediation report types | **Partial** | B | One layout contains all sections; there is no per-audience template or report-type selector. |
| REP-05 | Framework readiness for ISO 27001, NIST CSF, CIS, SOC 2, HIPAA, GDPR and PCI-DSS | **Delivered** | B | `FrameworkMapper` also covers ISO 42001 and NIST AI RMF; coverage and gap lists per framework. |
| REP-06 | Map Purview controls to framework requirements and highlight gaps | **Delivered** | T | Per-control framework references feed coverage percentages and named gaps. |

## P. Security engineering

| ID | Requirement | Status | Proof | Evidence / gap |
| --- | --- | --- | --- | --- |
| SEC-01 | Microsoft SDL practices | **Partial** | D | SDL-style practices are documented and partly enforced in CI; there is no formal SDL process record (training, bug bar, security sign-off). |
| SEC-02 | OWASP Top 10 and ASVS | **Delivered** | D | Reviewed in `docs/03` §6 with per-item status. |
| SEC-03 | CIS Controls | **Delivered** | D | CIS v8 mappings per control and in the security review. |
| SEC-04 | NIST SSDF | **Partial** | D | Mapped in prose; no SSDF practice evidence or attestation. |
| SEC-05 | Secure coding and least privilege | **Delivered** | B | Read-only collectors, parameterised SQL, encoded report output, no write cmdlets in the embedded script, per-user install. |
| SEC-06 | RBAC | **Partial** | D | Tenant-side role guidance (Purview role groups, PIM) is assessed; the application itself has no roles. |
| SEC-07 | Dependency scanning | **Delivered** | D | CI runs `dotnet list package --vulnerable --include-transitive`; two direct third-party dependencies. |
| SEC-08 | Vulnerability scanning | **Delivered** | D | Same CI gate; no container or binary scanner because there is no container. |
| SEC-09 | Threat modelling | **Delivered** | D | STRIDE model with trust boundaries in `docs/03` §5. |
| SEC-10 | Audit logging | **Delivered** | T | Local audit log for assessments, approvals, AI decisions and executions; no tamper-evident chaining (see SEC-13). |
| SEC-11 | Code signing | **Not delivered** | D | Build script and CI support signing when a certificate is supplied; no certificate exists, so nothing is signed. |
| SEC-12 | Secrets management | **Delivered** | T | Provider keys resolve from environment variables and are never persisted. The lab Entra client secret is DPAPI-protected (`CurrentUser`) in the settings document and decrypted transparently on load; legacy plaintext is encrypted on next save. Test: `Settings_ProtectsClientSecretAtRest`. |
| SEC-13 | Tamper detection | **Delivered** | T | SHA-256 chain over insertion order with legacy backfill, `IAuditLog.VerifyAsync`, and a Settings verification button. Detects edited or deleted rows at the first affected entry. Tests: `AuditChain_VerifiesInOrder`, `AuditChain_DetectsTampering`. |
| SEC-14 | Encryption at rest | **Delivered** | T | Assessment, change-plan, approval and backup payloads are DPAPI-encrypted (`dpapi:v1:`) before they are written and decrypted on read; legacy rows still load. Tenant ids, timestamps, scores and SQLite pages remain plaintext, so database page encryption (SQLCipher) is still a hardening step. Test: `AssessmentEvidence_IsEncryptedAtRest_AndStillLoads`. |
| SEC-15 | Encryption in transit | **Delivered** | B | HTTPS only for Graph, Key Vault and AI providers; TLS validated by the platform handler. |
| SEC-16 | Secure auto-update | **Partial** | T | Settings checks an https update manifest, validates version and download URL, requires a 64-hex SHA-256, and writes the MSI only after the downloaded bytes match that hash; installation remains manual. No Authenticode signing or hosted manifest. Tests: `UpdateCheckerTests`. |

## Q. Architecture and technology

| ID | Requirement | Status | Proof | Evidence / gap |
| --- | --- | --- | --- | --- |
| ARC-01 | .NET 9 and C# | **Delivered** | T | All five projects target .NET 9; solution builds in Release. |
| ARC-02 | WinUI 3 preferred shell | **Not delivered** | — | WPF was chosen because WinUI 3 could not be built or verified in this environment. Core layers are UI-agnostic. |
| ARC-03 | WPF fallback shell | **Delivered** | B | WPF shell with MVVM, dark theme, async commands. |
| ARC-04 | MVVM | **Delivered** | B | ViewModels, `ObservableObject`, `RelayCommand`, `AsyncRelayCommand`; no code-behind logic. |
| ARC-05 | Microsoft Graph SDK | **Partial** | B | Graph is consumed through typed `HttpClient` calls against v1.0/beta REST, not the SDK. |
| ARC-06 | REST APIs | **Delivered** | B | Graph, Key Vault and all AI provider calls are REST. |
| ARC-07 | SQLite | **Delivered** | T | Eight tables, WAL-friendly pooling, round-trip test. |
| ARC-08 | Optional SQL Server | **Not delivered** | — | No SQL Server port or implementation. |
| ARC-09 | Clean architecture, SOLID, DRY, dependency injection | **Delivered** | B | Four layers with ports/adapters and one composition root; rules are data-driven rather than duplicated per module. |
| ARC-10 | Repository, factory and strategy patterns | **Delivered** | B | Stores as repositories, `AiProviderFactory`, strategy-shaped providers and modules. |

## R. User experience

| ID | Requirement | Status | Proof | Evidence / gap |
| --- | --- | --- | --- | --- |
| UX-01 | Separate executive, security, governance and compliance dashboards | **Delivered** | B | The dashboard now has Executive, Security, Governance and Compliance tabs with persona-specific summaries, module filters and framework coverage. |
| UX-02 | Risk heatmaps | **Delivered** | T | `RiskHeatmap` groups open findings by module × severity and the Security tab renders it as intensity-scaled cells. Test: `Heatmap_GroupsOpenFindingsAndIgnoresPassingControls`. |
| UX-03 | Maturity tracking | **Delivered** | T | History is loaded per tenant and the dashboard shows the maturity trail (previous → current over N assessments) plus the overall delta. Tests cover the history series. |
| UX-04 | Trends | **Delivered** | T | Executive tab charts the last twelve overall scores from stored assessments, oldest to newest. Test: `Trend_IsAscending_AndKeepsTheNewestPoints`. |
| UX-05 | Recommendations | **Delivered** | T | Prioritised, licence-aware recommendations with effort and change risk. |
| UX-06 | Action center | **Partial** | B | Priority findings are listed; no owner, due date, status or assignment workflow. |

## S. Help center and About

| ID | Requirement | Status | Proof | Evidence / gap |
| --- | --- | --- | --- | --- |
| HLP-01 | Beginner-friendly topics for Purview, DLP, labels, DSPM, eDiscovery, insider risk, AI governance, Compliance Manager and Copilot governance | **Delivered** | B | 12 searchable topics in `HelpViewModel`, including every named subject plus scoring and the privacy guard. |
| HLP-02 | Searchable documentation | **Delivered** | B | Live filter across title, category and body. |
| ABT-01 | About page: product, designed by Raj Raja, owned by Belle-RajaCorp, contact@belle-rajacorp.com | **Delivered** | B | Exact strings in `AboutViewModel`; also in README and installer metadata. |

## T. Installer and enterprise deployment

| ID | Requirement | Status | Proof | Evidence / gap |
| --- | --- | --- | --- | --- |
| INS-01 | MSI/WiX project | **Delivered** | B | `installer/PurviewGovernanceCopilot.wixproj` + `Package.wxs` harvest the publish output into a per-machine package with major upgrade, Start Menu and desktop shortcuts. Built here with WiX 5.0.2 (`dotnet build … -p:PublishDir=…`). |
| INS-02 | MSIX package | **Partial** | D | `installer/msix/AppxManifest.xml` only; there is no packaging project, assets or build step producing an `.msix`. |
| INS-03 | Silent install | **Delivered** | D | `msiexec /i ... /qn` documented and used by the WiX package properties; not executed in this environment. |
| INS-04 | Intune deployment | **Delivered** | D | Documented Win32/MSI Intune commands in `docs/05` §4. |
| INS-05 | MECM/SCCM deployment | **Delivered** | D | Documented application model steps in `docs/05` §4. |
| INS-06 | Upgrade and rollback | **Partial** | D | `MajorUpgrade` handles forward upgrades. Rollback means reinstalling the previous MSI; there is no automatic downgrade path. |
| INS-07 | Produced installer binaries | **Delivered** | B | `dist/PurviewGovernanceCopilot-1.0.0.msi` (1.8 MB, framework-dependent, with a SHA-256 file) is committed and passes `wix msi validate` ICE checks. Unsigned; no MSIX binary. |

---

## Summary

| Status | Count | Share |
| --- | --- | --- |
| **Delivered** | 94 | 70% |
| **Partial** | 32 | 24% |
| **Not delivered** | 8 | 6% |
| **Total tracked** | 134 | 100% |

By section:

| Section | Delivered | Partial | Not delivered |
| --- | --- | --- | --- |
| Platform and scope | 2 | 2 | 0 |
| Authentication | 9 | 3 | 0 |
| Discovery and assessment | 10 | 4 | 0 |
| Deployment and change control | 1 | 3 | 1 |
| Modular architecture | 1 | 2 | 0 |
| AI assistant and failover | 11 | 0 | 1 |
| AI privacy guardrail | 11 | 1 | 0 |
| Change management and safety | 4 | 3 | 0 |
| Multi-tenant MSP | 1 | 1 | 1 |
| Configuration-as-code | 2 | 0 | 1 |
| Approval workflow | 2 | 0 | 0 |
| Offline and regulated | 2 | 0 | 1 |
| Licence optimisation | 1 | 1 | 0 |
| Continuous monitoring | 1 | 3 | 0 |
| Reporting | 5 | 1 | 0 |
| Security engineering | 11 | 4 | 1 |
| Architecture and technology | 7 | 1 | 2 |
| User experience | 5 | 1 | 0 |
| Help and About | 3 | 0 | 0 |
| Installer | 5 | 2 | 0 |

## What is genuinely missing

Ranked by risk to the commercial claim, not by effort:

1. **Per-customer RBAC and cross-tenant export (MSP-02, MSP-03)** — tenant switching, isolation and the portfolio view exist, but there are no user roles and no combined report.
2. **Live deployment execution (DEP-05)** — by design, but the specification asks for apply and rollback; the reviewed script and reversal script exist, execution does not.
3. **Security Copilot (AIA-02)** and **government clouds (OFF-03)**.
4. **Config-as-code breadth (CAC-02)** — no ARM/Bicep/Terraform output, and existing tenant objects are not exported individually.
5. **Unattended scheduling and external alerts (MON-02, MON-04)** — checks run only while the app is open and alerts stay in-app.
6. **Guided onboarding wizard (AUT-12)** and **Key Vault wiring (AUT-04)**.
7. **Action centre ownership (UX-06)** — findings have no owner, due date or status.
8. **Code signing, MSIX packaging and a hosted update manifest (SEC-11, SEC-16, INS-02)** — the MSI is produced but unsigned, the updater has no published manifest, and there is no `.msix` build.
9. **SQL Server support (ARC-08)** and **WinUI 3 (ARC-02)**.
10. **DSPM, Compliance Manager and Defender data collection (DSC-08, DSC-11)**.
11. **SQLCipher page encryption and per-customer RBAC (SEC-14 metadata, MSP-03)** — sensitive payloads are now encrypted, but database pages and tenant metadata are not, and all saved tenants remain reachable by one Windows user.

## Verification performed

| Check | Result |
| --- | --- |
| `dotnet build PurviewGovernanceCopilot.sln -c Release` | Succeeded (local SDK 9.0.321) |
| `dotnet test tests/PurviewCopilot.Tests -c Release` | 80 passed, 0 failed |
| Catalog count (`grep -c 'Add("' ModuleCatalog.cs`) | 49 rules across 16 modules |
| Exporter inventory | HTML, JSON, CSV, Markdown, PDF, DOCX, XLSX |
| `ReportFormat` enum | `Html, Json, Csv, Markdown, Pdf, Docx, Xlsx` |
| Artefact search (`*.bicep`, `*.tf`, `*arm*.json`, `*.docx`, `*.xlsx`) | None found |
| MSP / SQL Server / Terraform references in `src` | None found |
| Key Vault usage in `src/PurviewCopilot.UI` | Not constructed; class exists in Infrastructure |
| Release executable smoke launch | Started and stayed alive; UI not visually inspected |

Claims that could not be verified here: anything requiring a tenant, Entra app registration, certificate in a real store, Key Vault, AI provider keys, WiX toolset or a code-signing certificate. Those are marked **C** or **D** above rather than asserted as working.
