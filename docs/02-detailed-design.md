# 02 — Detailed Design

## 1. Database schema (SQLite)

Database: `%LOCALAPPDATA%\PurviewGovernanceCopilot\purviewcopilot.db`  
Pragmas: `journal_mode=WAL`, `foreign_keys=ON`. Schema is created idempotently on startup.

| Table | Purpose | Key columns |
| --- | --- | --- |
| `assessments` | Immutable assessment records | `id` PK, `tenant_id`, `run_utc`, `mode`, `overall`, `payload` (JSON: scores, findings, recommendations) |
| `change_plans` | Change plans and their state | `id` PK, `tenant_id`, `created_utc`, `state`, `payload` |
| `approvals` | Approval requests, one per plan | `id` PK, `plan_id` UNIQUE, `state`, `updated_utc`, `payload` (stage history) |
| `backups` | Pre-change configuration packages | `id` PK, `tenant_id`, `created_utc`, `label`, `payload` |
| `audit_log` | Local audit trail | `id` PK, `at_utc`, `actor`, `action`, `target`, `detail`, `correlation_id` |
| `ai_usage` | Provider attempts, tokens and cost | `id` PK, `provider_id`, `model`, `prompt_tokens`, `completion_tokens`, `cost`, `succeeded`, `error_code`, `at_utc` |
| `settings` | Single JSON document | `key` PK (`'app'`), `value` |
| `tenant_profiles` | MSP customer tenants and per-customer access | `tenant_id` PK, `updated_utc`, `payload` |
| `finding_actions` | Action-centre owner, due date, status and encrypted notes | `tenant_id`, `finding_id`, `updated_utc`, `payload` |
| `baselines` | Reserved for persisted drift baselines | `tenant_id` PK, `updated_utc`, `payload` |

Indexes: `assessments(tenant_id, run_utc DESC)`, `change_plans(tenant_id, created_utc DESC)`, `backups(tenant_id, created_utc DESC)`, `audit_log(at_utc DESC)`, `ai_usage(at_utc DESC)`.

Retention: the app never deletes audit or approval records automatically. A future admin control will add retention windows; until then the file is small (JSON payloads) and can be archived by the operator.

### JSON payload contracts

- **Assessment**: `{ id, tenantId, runUtc, mode, scores, findings[], recommendations[], ruleCount }`.
- **Change plan**: `{ id, tenantId, mode, title, recommendationIds[], operations[], impact{}, createdUtc, createdBy, simulationRequired }`.
- **Approval**: `{ id, planId, state, currentStageIndex, history[], createdUtc, updatedUtc }`.
- **Settings**: `{ auth{mode,tenantId,clientId,certificateThumbprint,clientSecret,managedIdentityClientId}, redactionMode, cloudAiEnabled, offlineMode, requirePromptReview, certificateExpiryWarningDays, keyVaultUri, keyVaultSecretName, dataDirectory, reportOutputDirectory, defaultMode, providers[] }`.

Secrets policy: `clientSecret` is only for lab mode and is stored in the local settings document — the UI labels this. Provider keys are never stored: `ApiKeyRef` names an **environment variable**.

## 2. External API surface (consumed)

All Graph calls are GET, `Authorization: Bearer`, `ConsistencyLevel: eventual`.

| Endpoint | Fact | Permission |
| --- | --- | --- |
| `/organization?$select=id,displayName` | tenant identity | `Organization.Read.All` |
| `/domains?$select=id,isInitial` | initial domain for PowerShell `-Organization` | `Domain.Read.All` |
| `/subscribedSkus` | licence inventory, Copilot seats | `Organization.Read.All` |
| `/users?$filter=accountEnabled eq true&$count=true` | enabled users | `User.Read.All` |
| `/users?$filter=userType eq 'Guest'&$count=true` | guests | `User.Read.All` |
| `/groups`, `/servicePrincipals`, `/teams`, `/sites` ($count) | estate size | `Group.Read.All`, `Application.Read.All`, `Team.ReadBasic.All`, `Sites.Read.All` |
| `/applications?$select=id,passwordCredentials` | app registrations using secrets | `Application.Read.All` |
| `/directoryRoles?$filter=roleTemplateId eq '62e90394-…'` + `/members/$count` | standing Global Administrators | `RoleManagement.Read.Directory` |
| `/identity/conditionalAccess/policies` (beta) | CA coverage | `Policy.Read.All` |
| `/security/secureScores?$top=1` | secure score | `SecurityEvents.Read.All` |
| `/deviceManagement/managedDevices?$count=true` | endpoint onboarding proxy | `DeviceManagementManagedDevices.Read.All` |
| `/reports/authenticationMethods/userRegistrationDetails` | MFA registration | `AuditLog.Read.All` |

Security & Compliance PowerShell (app-only certificate):

| Cmdlet | Fact |
| --- | --- |
| `Get-Label`, `Get-LabelPolicy`, `Get-AutoSensitivityLabelRule` | labels and publishing |
| `Get-DlpCompliancePolicy` | DLP count, mode, endpoint locations |
| `Get-RetentionCompliancePolicy`, `Get-ComplianceTag` | lifecycle and records |
| `Get-InsiderRiskPolicy`, `Get-SupervisoryReviewPolicyV2` | insider risk, communication compliance |
| `Get-ComplianceCase` | eDiscovery, premium detection |
| `Get-AdminAuditLogConfig`, `Get-UnifiedAuditLogRetentionPolicy`, `Get-ProtectionAlert` | audit state and alerting |

The generated script is stored at `%LOCALAPPDATA%\PurviewGovernanceCopilot\scripts\Get-PurviewFacts.ps1` and is auditable text. It contains no secrets: the thumbprint and app ID are non-secret identifiers.

## 3. Internal contracts

| Port | Responsibility |
| --- | --- |
| `ITenantCollector` | produce a `TenantSnapshot` |
| `IAssessmentStore` / `IChangePlanStore` / `IApprovalStore` / `IBackupStore` | persistence |
| `IAuditLog` / `IAiUsageStore` / `ISettingsStore` | audit, metering, configuration |
| `ICertificateService` | certificate lifecycle |
| `IKeyVaultCertificateStore` | optional central PFX storage |
| `IAiProvider` / `IAiProviderFactory` | provider adapter and construction |
| `IChangeExecutor` | simulate / apply / generate plan and rollback scripts |
| `IReportExporter` | one renderer per format |

## 4. UI information architecture

```
Shell (nav rail + header + status bar)
├── Executive Dashboard   scores · module coverage · priority findings
├── Assessment            run live/sample · filter · findings · recommendations
├── Deployment            mode · selection · plan · simulate · submit · approve/reject
├── Monitoring            drift · licence utilisation · optimisation insights
├── Reports               framework coverage · export HTML/JSON/CSV/Markdown
├── AI Assistant          question · sanitized prompt · conversation · usage
├── Settings               connection · certificates · AI providers · privacy
├── Help Center           12 beginner-friendly topics, searchable
└── About                 product, authorship, contact, platform support
```

### Wireframes (text)

**Dashboard**

```
┌ Tenant card: name · run time · maturity · failures · warnings · recommendations ┐
│ [Overall] [Security] [Governance] [Compliance]                                  │
│ [Risk]    [AI gov]   [Copilot readiness]                                        │
├──────────────────────────────┬──────────────────────────────────────────────────┤
│ Module coverage  ▓▓▓▓▓░░ 78  │ Priority findings                                │
│ Audit            ▓▓▓▓▓▓▓ 92  │ ▌AUD-02  Audit retention below 365 days          │
│ DLP              ▓▓▓░░░░ 44  │ ▌DLP-02  Policies in simulation only             │
└──────────────────────────────┴──────────────────────────────────────────────────┘
```

**Deployment**

```
[Mode ▾] [Operator ____] [Build change plan]
┌ Recommendations (420px) ─────────────┬ Change-management pack ──────────────┐
│ ☑ REC-DLP-02 …                       │ [Simulate] [Submit] [Approve] …      │
│ ☑ REC-AUD-02 …                       │ Approval state: PendingSecurity      │
│ ☐ REC-CPG-01 …                       │ Executive / technical / impacts …    │
└──────────────────────────────────────┴ Simulation output (monospace) ───────┘
```

**Settings**

```
Connection: auth mode · tenant · client · thumbprint · [Test] [Save]
┌ Certificates ──────────────┬ Privacy guard ─────────────┬ AI providers ──────┐
│ subject/months [Create]    │ mode ▾                     │ ☐ Ollama           │
│ [Import][Export][Rotate]   │ ☑ cloud AI  ☐ offline      │ ☐ OpenAI           │
│ thumbprint · expiry list   │ ☑ review before send       │ ☐ Anthropic …      │
└────────────────────────────┴────────────────────────────┴────────────────────┘
```

## 5. Scoring specification

For each module with at least one assessed control:

```
score(module) = Σ points(control) / count(assessed controls)
points: Pass = 100, Warning = 60, Fail = 0, NotAssessed = excluded
```

Domain scores are weighted averages over their modules (unassessed modules excluded). Weights: Audit 1.5, DLP 1.5, Information Protection 1.3, Endpoint DLP 1.2, Insider Risk 1.2, AI Governance 1.2, Copilot Governance 1.1, others 1.0.

Risk exposure is independent of the 0–100 quality scores:

```
risk = min(100, Σ severityWeight × (warning ? 0.4 : 1.0))
severityWeight: Critical 25, High 15, Medium 8, Low 3, Info 1
```

Maturity band from overall: ≥85 Optimized, ≥70 Managed, ≥50 Defined, ≥30 Developing, else Initial.

## 6. Approval state machine

```
Draft ──submit──▶ PendingSecurity ──approve──▶ PendingCompliance ──approve──▶ PendingCab ──approve──▶ Approved
  ▲                     │                            │                          │
  └── request changes ──┴────────────────────────────┴──────────────────────────┘
                        │ reject (any stage) ──▶ Rejected
Approved ──apply──▶ Applied        Applied ──rollback──▶ RolledBack
```

Invalid transitions throw; the history array is append-only with stage, decision, actor, comment and timestamp.

## 7. Validation plan per change

Every plan carries post-deployment validation steps and rollback steps. Simulation produces the exact PowerShell that would run, including a pre-flight payload validation that fails the simulation on an empty payload. Reports for a plan are keyed to the assessment ID so evidence can be reconstructed later.
