# 01 — Solution Architecture

## 1. Goals and constraints

The application must assess and govern Microsoft Purview without becoming a new data-egress risk, and without requiring administrator rights on the workstation.

| Driver | Architectural consequence |
| --- | --- |
| Least privilege and read-only assessment | Discovery is isolated behind `ITenantCollector`; all Graph calls are GET; Purview collection uses a read-only PowerShell script |
| Auditability | Deterministic scoring, local SQLite audit log, immutable approval history, backup before change |
| Privacy | AI traffic passes a local guard port; providers receive sanitized text only |
| Extensibility | Modules are data-driven rules registered in one catalog; providers are factory-created |
| Offline/regulated use | No cloud dependency for assessment or reporting; local AI providers supported; SQLite only |
| Windows-only, commercial | WPF on .NET 9, per-user install, MSI/MSIX packaging, code-signing hooks in CI |

WinUI 3 was considered (and remains the preferred future shell), but the verified build target in this environment is WPF: it ships in-box with the Windows Desktop runtime, needs no Windows App SDK deployment step, and keeps the solution buildable with the .NET SDK alone. The UI-agnostic Domain/Application/Infrastructure layers mean a WinUI shell can be added without touching business logic.

## 2. Layers

```
┌─────────────────────────────────────────────────────────────┐
│ PurviewCopilot.UI (WPF, MVVM)                               │
│ Shell · 9 pages · converters · theme · composition root      │
└───────────────────────────┬─────────────────────────────────┘
                            │ depends on
┌───────────────────────────▼─────────────────────────────────┐
│ PurviewCopilot.Application                                  │
│ GovernanceWorkspace · AssessmentService · ModuleCatalog      │
│ ChangePlanBuilder · ApprovalWorkflow · BackupService         │
│ AiFailoverOrchestrator · CopilotService · DriftDetector      │
│ LicenseOptimizer · FrameworkMapper · Report exporters        │
│ Ports: ITenantCollector, I*Store, I*Provider, IChangeExecutor│
└───────────────────────────┬─────────────────────────────────┘
                            │ depends on
┌───────────────────────────▼─────────────────────────────────┐
│ PurviewCopilot.Domain                                       │
│ Models · Enums · ScoringEngine · SensitiveDataDetector       │
│ RedactionService        (no I/O, no framework references)    │
└───────────────────────────▲─────────────────────────────────┘
                            │ implements ports
┌───────────────────────────┴─────────────────────────────────┐
│ PurviewCopilot.Infrastructure                               │
│ GraphTenantCollector · CompliancePowerShellCollector         │
│ SqliteStore · WindowsCertificateService · EntraTokenProvider │
│ KeyVaultCertificateStore · AiProviderFactory · ChangeExecutor│
└─────────────────────────────────────────────────────────────┘
```

Dependency rule: Domain ← Application ← Infrastructure, and UI → Application (never Infrastructure types in view models, except the composition root which may reference both).

`PurviewCopilot.Domain` has zero package references. `Application` has zero package references. `Infrastructure` has exactly two: `Azure.Identity` (credential chain) and `Microsoft.Data.Sqlite` (local store). This keeps the supply-chain and vulnerability surface small and makes dependency scanning meaningful.

## 3. Folder structure

```
src/PurviewCopilot.Domain/
  Enums.cs, Models.cs
  Scoring/ScoringEngine.cs
  Privacy/SensitiveDataDetector.cs, RedactionService.cs
src/PurviewCopilot.Application/
  Abstractions/Ports.cs            # every outbound port + AppSettings
  Modules/ControlRule.cs, ModuleCatalog.cs, StandardModule.cs
  Assessment/AssessmentService.cs
  Deployment/ChangePlanBuilder.cs, BackupService.cs
  Approvals/ApprovalWorkflow.cs
  Ai/AiFailoverOrchestrator.cs, CopilotService.cs
  Licensing/LicenseOptimizer.cs
  Monitoring/DriftDetector.cs
  Reporting/ReportExporters.cs, FrameworkMapper.cs
  Demo/DemoTenant.cs
  Workspace/GovernanceWorkspace.cs
src/PurviewCopilot.Infrastructure/
  Auth/            EntraTokenProvider, WindowsCertificateService, KeyVaultCertificateStore, ConnectionVerifier
  Discovery/       GraphTenantCollector, CompliancePowerShellCollector, CompositeTenantCollector
  Data/            SqliteStore
  Ai/              AiProviders (OpenAI-compatible, Anthropic, Gemini, factory)
  Deployment/      ChangeExecutor
src/PurviewCopilot.UI/
  App.xaml(.cs), AppServices.cs, MainWindow.xaml(.cs)
  Mvvm/, ViewModels/, Views/, Converters/, Styles/
tests/PurviewCopilot.Tests/
```

## 4. Assessment pipeline

```
AuthConfiguration
   │
   ▼
CompositeTenantCollector
   ├── GraphTenantCollector        (licensing, users, roles, CA, secure score, devices, app secrets)
   └── CompliancePowerShellCollector (labels, DLP, retention, audit, IRM, CC, eDiscovery)
   │        every call individually fault-tolerant → TenantSnapshot.Warnings
   ▼
TenantSnapshot  (normalized facts; modules never call the network)
   │
   ▼
ModuleRegistry (16 StandardModule instances over 48 ControlRules)
   ├── AssessAsync   → Finding(Pass|Warning|Fail, severity, evidence, remediation, frameworks)
   └── RecommendAsync→ Recommendation(priority, effort, change risk, licences, operations)
   │
   ▼
ScoringEngine.Compute  → ScoreSet (deterministic)
   │
   ▼
AssessmentResult → SqliteStore + audit entry
```

The snapshot indirection is the key testability decision: every control is a pure function of data, so the whole assessment and scoring pipeline is covered by unit tests with no tenant.

## 5. Module architecture and plugin model

A module is a `ModuleDescriptor` (key, name, description, licences, frameworks) plus its rules. `StandardModule` implements assessment and recommendation generation for any rules; behaviour comes from data. Adding a control is a catalog entry; adding a module is a descriptor plus rules. If a module ever needs behaviour the generic path cannot express, it can implement `IModule` directly and be registered in `ModuleRegistry` without changing callers — that is the plugin seam.

Each module supports discovery (facts it consumes), assessment, recommendations, deployment (operations on its recommendations), validation (post-checks in the change pack), reporting (framework refs), rollback (backup/rollback steps) and monitoring (drift rules that reference its facts).

48 controls currently cover: Information Protection, Sensitivity Labels, Auto Labelling, DLP, Endpoint DLP, Data Classification, Records Management, Data Lifecycle, Audit, Insider Risk, Communication Compliance, eDiscovery, DSPM, Compliance Manager (incl. identity controls), AI Governance and Copilot Governance.

## 6. Deployment modes

| Mode | Selection behaviour |
| --- | --- |
| Assessment Only | No plan permitted; reporting and monitoring only |
| Guided Deployment | Operator selects recommendations; per-item review |
| One-Click Recommended | All high/critical recommendations preselected; still simulated and approved |
| Custom | Free selection, including low-priority items |
| Expert | Full operation list exposed; destructive operations flagged |
| Greenfield | Same pipeline with a baseline package designed for empty tenants |

Every mode shares one guarantee: a plan cannot be applied without simulation and approval, and a pre-change backup is captured at plan creation.

## 7. AI orchestration

```
Question + assessment summary
        │
        ▼
CopilotService.PrepareAsync
  └── RedactionService (mode: redact | mask | tokenize | block)
        ├── blocked → PrivacyGuardException, audited, nothing sent
        └── sanitized → PreparedPrompt (visible to administrator)
        ▼
AiFailoverOrchestrator.CompleteAsync
  └── providers by priority (local-only when offline/cloud disabled)
        ├── 402/429 quota → next provider
        ├── 401/403 auth   → next provider
        ├── timeout/5xx    → next provider
        └── success → AiResponse + usage record (tokens, cost, provider)
        ▼
Local audit log (categories only — never raw values)
```

Provider adapters share an HTTP base that classifies failures; quota detection is what makes free-tier exhaustion fail over rather than fail.

## 8. Persistence

SQLite in `%LOCALAPPDATA%\PurviewGovernanceCopilot\purviewcopilot.db`, WAL mode, schema created idempotently at startup. One `SqliteStore` implements every storage port. Optional SQL Server would be a second implementation of the same ports; no application code changes.

## 9. Non-functional characteristics

- **Startup**: composition root wires ~20 objects; no container, no network calls, no migrations to run — UI shows immediately.
- **Offline**: assessment against a snapshot, scoring, reporting and local AI work with no internet.
- **Failure isolation**: a missing Graph permission degrades one fact and records a warning; a missing cmdlet adds a note; neither aborts the assessment.
- **Determinism**: no randomness in scoring; report output for the same assessment is byte-stable except timestamps.
- **Memory**: snapshots and findings are small; report generation streams into a string builder.
